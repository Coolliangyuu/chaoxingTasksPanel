param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$WorkDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($WorkDirectory)
$workspace=[IO.Path]::GetFullPath((Get-Location).Path).TrimEnd('\')+'\'
if(-not $root.StartsWith($workspace,[StringComparison]::OrdinalIgnoreCase)){throw 'Choice fixture must remain in the workspace'}
New-Item -ItemType Directory -Force $root | Out-Null
$fixture=Join-Path $root 'choice-only.msi'
Copy-Item -LiteralPath (Resolve-Path -LiteralPath $Installer).Path -Destination $fixture
$engine=New-Object -ComObject WindowsInstaller.Installer
$db=$engine.OpenDatabase($fixture,1)
function Sql([string]$query){$view=$db.OpenView($query);[void]$view.Execute();[void]$view.Close();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)}
Sql ('UPDATE `Property` SET `Value`=''{0}'' WHERE `Property`=''ProductCode''' -f ('{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'))
Sql 'DELETE FROM `InstallExecuteSequence`'
Sql 'DELETE FROM `InstallUISequence`'
Sql 'DELETE FROM `MsiEmbeddedUI`'
Sql 'DELETE FROM `CustomAction` WHERE `Action`=''CleanupStartup'''
[void]$db.Commit()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine)
$exe=Join-Path $root 'UninstallChoiceChecks.exe'
& g++.exe '-std=c++17' '-O2' '-static' '-municode' (Join-Path $PSScriptRoot 'UninstallChoiceChecks.cpp') '-lmsi' '-o' $exe
if($LASTEXITCODE){throw 'Choice checks failed to compile'}
& $exe $fixture
if($LASTEXITCODE){throw 'Choice checks failed'}
