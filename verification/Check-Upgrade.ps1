param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$WorkDirectory,[switch]$WithRollback)
$ErrorActionPreference='Stop'
if($WithRollback -and -not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){
 throw 'Rollback registration testing requires an administrator token; use the default run for isolated non-elevated upgrade checks.'
}
$root=[IO.Path]::GetFullPath($WorkDirectory)
$workspace=[IO.Path]::GetFullPath((Get-Location).Path).TrimEnd('\')+'\'
if(-not $root.StartsWith($workspace,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixtures must stay in the current workspace'}
New-Item -ItemType Directory -Path $root -Force | Out-Null
$template=(Resolve-Path -LiteralPath $Installer).Path
$family='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
$component='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
$registry='Software\CodexUpgradeTests\'+[Guid]::NewGuid().ToString('N')
$engine=New-Object -ComObject WindowsInstaller.Installer
$engine.UILevel=2
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class UpgradeMsi {
 [DllImport("msi.dll",CharSet=CharSet.Unicode)] public static extern uint MsiInstallProductW(string path,string options);
 [DllImport("msi.dll",CharSet=CharSet.Unicode)] public static extern int MsiQueryProductStateW(string product);
 [DllImport("msi.dll",CharSet=CharSet.Unicode)] public static extern uint MsiConfigureProductExW(string product,int level,int state,string options);
 [DllImport("msi.dll")] public static extern uint MsiSetInternalUI(uint level,IntPtr window);
 [DllImport("msi.dll",CharSet=CharSet.Unicode)] public static extern uint MsiEnableLogW(uint mode,string path,uint attributes);
}
'@
[void][UpgradeMsi]::MsiSetInternalUI(2,[IntPtr]::Zero)
function Assert([bool]$ok,[string]$message){if(-not $ok){throw $message};Write-Output "PASS: $message"}
function Sql($db,[string]$query){$view=$db.OpenView($query);try{[void]$view.Execute()}catch{throw "Fixture SQL failed: $query :: $($_.Exception.Message)"}finally{[void]$view.Close();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}}
function Literal([string]$value){return "'"+$value.Replace("'","''")+"'"}
function Value($db,[string]$query){$view=$db.OpenView($query);try{[void]$view.Execute();$record=$view.Fetch();if($record){return $record.StringData(1)};return ''}finally{[void]$view.Close();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}}
function SetProperty($db,[string]$name,[string]$value){
 Sql $db ('DELETE FROM `Property` WHERE `Property`='+ (Literal $name))
 Sql $db ('INSERT INTO `Property` (`Property`,`Value`) VALUES ('+(Literal $name)+','+(Literal $value)+')')
}
$fixtures=New-Object 'Collections.Generic.List[object]'
# A tiny inert versioned executable; it is installed as data and never executed.
$source=Join-Path $root 'fixture.cpp'
[IO.File]::WriteAllText($source,'#include <windows.h>'+"`n"+'int WINAPI WinMain(HINSTANCE,HINSTANCE,LPSTR,int){return 0;}')
$resource=Join-Path $root 'fixture.rc'
@'
#include <windows.h>
1 VERSIONINFO
FILEVERSION 1,5,0,0
PRODUCTVERSION 1,5,0,0
FILEOS 0x40004
FILETYPE 1
BEGIN
 BLOCK "StringFileInfo"
 BEGIN
  BLOCK "040904b0"
  BEGIN
   VALUE "FileVersion", "1.5.0.0\0"
   VALUE "ProductVersion", "1.5.0.0\0"
  END
 END
 BLOCK "VarFileInfo"
 BEGIN
  VALUE "Translation", 0x409, 1200
 END
END
'@ | Set-Content -LiteralPath $resource -Encoding ascii
& windres.exe $resource -O coff -o (Join-Path $root 'fixture.o')
if($LASTEXITCODE){throw 'Fixture resource compilation failed'}
& g++.exe '-Os' '-static' '-s' '-mwindows' $source (Join-Path $root 'fixture.o') '-o' (Join-Path $root 'fixture-base.exe')
if($LASTEXITCODE){throw 'Fixture executable compilation failed'}

function New-Fixture([string]$name,[long]$build,[string]$version='1.5.0',[bool]$legacy=$false,[bool]$fail=$false) {
 $directory=Join-Path $root $name
 New-Item -ItemType Directory -Path $directory -Force | Out-Null
 $payload=Join-Path $directory 'AppExe'
 $bytes=[IO.File]::ReadAllBytes((Join-Path $root 'fixture-base.exe'))
 $marker=[Text.Encoding]::UTF8.GetBytes("`nfixture=$name`n")
 [IO.File]::WriteAllBytes($payload,([byte[]]($bytes+$marker)))
 $cabinet=Join-Path $directory 'fixture.cab'
 & "$env:SystemRoot/System32/makecab.exe" '/D' 'CompressionType=MSZIP' $payload $cabinet | Out-Null
 if($LASTEXITCODE){throw 'Fixture cabinet creation failed'}
 $msi=Join-Path $directory ($name+'.msi')
 Copy-Item -LiteralPath $template -Destination $msi
 $db=$engine.OpenDatabase($msi,1)
 $product='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
 SetProperty $db ProductCode $product
 SetProperty $db UpgradeCode $family
 SetProperty $db ProductName ('Codex isolated upgrade fixture '+$name)
 SetProperty $db ProductVersion $version
 SetProperty $db CODEX_UPGRADE_FIXTURE $family
 SetProperty $db ARPSYSTEMCOMPONENT '1'
 Sql $db 'DELETE FROM `Property` WHERE `Property`=''ALLUSERS'''
 if($legacy){Sql $db 'DELETE FROM `Property` WHERE `Property`=''INSTALLER_BUILD'''}else{SetProperty $db INSTALLER_BUILD ([string]$build)}
 Sql $db ('UPDATE `Component` SET `ComponentId`='+(Literal $component))
 Sql $db 'DELETE FROM `Upgrade`'
 Sql $db ('INSERT INTO `Upgrade` (`UpgradeCode`,`VersionMax`,`Attributes`,`ActionProperty`) VALUES ('+(Literal $family)+','+(Literal $version)+',513,''OLDPRODUCTS'')')
 Sql $db ('INSERT INTO `Upgrade` (`UpgradeCode`,`VersionMin`,`Attributes`,`ActionProperty`) VALUES ('+(Literal $family)+','+(Literal $version)+',2,''NEWERPRODUCTS'')')
 Sql $db ('UPDATE `Registry` SET `Root`=1,`Key`='+(Literal $registry))
 Sql $db ('UPDATE `RegLocator` SET `Root`=1,`Key`='+(Literal $registry))
 Sql $db 'DELETE FROM `MsiShortcutProperty`'
 Sql $db 'DELETE FROM `Shortcut`'
 Sql $db 'DELETE FROM `RemoveFile`'
 Sql $db 'DELETE FROM `MsiEmbeddedUI`'
 foreach($action in @('CleanupStartup','PrepareUninstallCleanup','PromptUninstallData','CreateShortcuts','RemoveShortcuts')){
  Sql $db ('DELETE FROM `InstallExecuteSequence` WHERE `Action`='+(Literal $action))
  Sql $db ('DELETE FROM `InstallUISequence` WHERE `Action`='+(Literal $action))
 }
 foreach($action in @('CleanupStartup','PrepareUninstallCleanup','PromptUninstallData')){
  Sql $db ('DELETE FROM `CustomAction` WHERE `Action`='+(Literal $action))
 }
 Sql $db ('UPDATE `File` SET `FileSize`='+[string](Get-Item -LiteralPath $payload).Length)
 $view=$db.OpenView('UPDATE `_Streams` SET `Data`=? WHERE `Name`=''app.cab''')
 $record=$engine.CreateRecord(1);[void]$record.SetStream(1,$cabinet);[void]$view.Execute($record);[void]$view.Close()
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record)
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
 if($fail){
  Sql $db 'INSERT INTO `CustomAction` (`Action`,`Type`,`Target`) VALUES (''FixtureFailure'',19,''Intentional upgrade rollback check'')'
  Sql $db 'INSERT INTO `InstallExecuteSequence` (`Action`,`Sequence`) VALUES (''FixtureFailure'',6500)'
 }
 $summary=$db.SummaryInformation(20);$summary.Property(15)=10
 $summary.Property(9)='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
 [void]$summary.Persist();[void]$db.Commit()
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
 # Refuse to install unless all integration paths and identities are isolated.
 if((Value $db 'SELECT `Value` FROM `Property` WHERE `Property`=''CODEX_UPGRADE_FIXTURE''') -ne $family){throw 'Missing fixture marker'}
 if((Value $db 'SELECT `ComponentId` FROM `Component`') -ne $component){throw 'Unisolated component'}
 if((Value $db 'SELECT `Key` FROM `Registry`') -ne $registry){throw 'Unisolated registry path'}
 if((Value $db 'SELECT `Shortcut` FROM `Shortcut`')){throw 'Fixture must not create shortcuts'}
 if((Value $db 'SELECT `FileKey` FROM `RemoveFile`')){throw 'Fixture must not remove external directories'}
 foreach($action in @('CleanupStartup','PrepareUninstallCleanup','PromptUninstallData')){
  if((Value $db ('SELECT `Action` FROM `CustomAction` WHERE `Action`='+(Literal $action)))){throw 'User-data cleanup must be absent from upgrade fixtures'}
 }
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
 $fixture=[PSCustomObject]@{Name=$name;Path=$msi;Product=$product;Hash=(Get-FileHash -LiteralPath $payload).Hash}
 $fixtures.Add($fixture);return $fixture
}
function Install-Fixture($fixture,[string]$options='') {
 if(-not $fixtures.Contains($fixture)){throw 'Only generated fixtures can be installed'}
 [void][UpgradeMsi]::MsiEnableLogW(0x3fff,(Join-Path $root ($fixture.Name+'-install.log')),0)
 return [UpgradeMsi]::MsiInstallProductW($fixture.Path,$options)
}
function Installed($fixture){return [UpgradeMsi]::MsiQueryProductStateW($fixture.Product) -eq 5}
function PayloadHash([string]$directory){return (Get-FileHash -LiteralPath (Join-Path $directory 'chaoxingTaskPanel.exe')).Hash}

try {
 $old=New-Fixture 'legacy' 0 '1.5.0' $true
 $new=New-Fixture 'new-revision' 200
 $older=New-Fixture 'older-revision' 100
 $failure=New-Fixture 'failed-upgrade' 300 '1.5.0' $false $true
 $future=New-Fixture 'future-version' 400 '1.6.0'
 $location=Join-Path $root '自定义 安装位置'
 $data=Join-Path $root 'user-data.txt'
 [IO.File]::WriteAllText($data,'preserve personal settings and reminders')
 Assert ((Install-Fixture $old ('INSTALLDIR="'+$location+'"')) -eq 0) 'Legacy version installs in private custom path'
 Assert ((PayloadHash $location) -eq $old.Hash) 'Legacy executable present'
 # Reusing a ProductCode after changing the public version triggers 1638.
 $collision=Join-Path $root 'legacy-collision.msi';Copy-Item -LiteralPath $old.Path -Destination $collision
 $db=$engine.OpenDatabase($collision,1);$summary=$db.SummaryInformation(20)
 SetProperty $db ProductVersion '1.5.1'
 $summary.Property(9)='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}';[void]$summary.Persist();[void]$db.Commit()
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
 $collisionStatus=[UpgradeMsi]::MsiInstallProductW($collision,'')
 Assert ($collisionStatus -eq 1638) ('Legacy repeated ProductCode reproduces error 1638 (actual '+$collisionStatus+')')
 Assert ((Install-Fixture $new) -eq 0) 'New MSI directly upgrades legacy same-version installation'
 Assert (-not (Installed $old) -and (Installed $new)) 'Old product removed and exactly new product installed'
 Assert ((PayloadHash $location) -eq $new.Hash) 'Same-version executable replaced in original Chinese path'
 $saved=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registry)
 Assert ($saved.GetValue('InstallDir').TrimEnd('\') -eq $location.TrimEnd('\')) 'Original installation directory remains recorded'
 $saved.Dispose()
 Assert ((Install-Fixture $new) -eq 0 -and (Installed $new)) 'Opening the exact same MSI again is supported'
 Assert ((Install-Fixture $older) -ne 0 -and (Installed $new) -and -not (Installed $older)) 'Older same-version revision is rejected'
 if($WithRollback){
  Assert ((Install-Fixture $failure) -ne 0) 'Deliberate upgrade failure reported'
  Assert ((Installed $new) -and -not (Installed $failure) -and (PayloadHash $location) -eq $new.Hash) 'Failed upgrade rolls back and restores prior product and executable'
 }else{Write-Output 'SKIP: Full rollback registration check requires an administrator token (use -WithRollback).'}
 Assert ((Install-Fixture $future) -eq 0 -and -not (Installed $new) -and (Installed $future)) 'Higher public version upgrades through same family'
 Assert ((Install-Fixture $new) -ne 0 -and (Installed $future)) 'Lower public version cannot replace newer version'
 Assert ([IO.File]::ReadAllText($data) -eq 'preserve personal settings and reminders') 'Unmanaged personal data survives the exercised operations'
 Write-Output ('Fixture family: '+$family)
}
finally {
 foreach($fixture in $fixtures){
  if([UpgradeMsi]::MsiQueryProductStateW($fixture.Product) -ne -1){
   [void][UpgradeMsi]::MsiEnableLogW(0x3fff,(Join-Path $root ($fixture.Name+'-cleanup.log')),0)
   $status=[UpgradeMsi]::MsiInstallProductW($fixture.Path,'REMOVE=ALL')
   if($status -ne 0){Write-Warning "Fixture cleanup failed: $($fixture.Product), status $status"}
  }
 }
 [void][UpgradeMsi]::MsiEnableLogW(0,$null,0)
 if([Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registry)){
  [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registry,$false)
 }
 [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine)
}
foreach($fixture in $fixtures){Assert ([UpgradeMsi]::MsiQueryProductStateW($fixture.Product) -eq -1) ('Temporary product cleaned: '+$fixture.Name)}
