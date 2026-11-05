param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$Output,
    [long]$BuildRevision = 0
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $Executable).Path
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$version = '{0}.{1}.{2}' -f $fileVersion.FileMajorPart,$fileVersion.FileMinorPart,$fileVersion.FileBuildPart
$productCode = '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
if ($BuildRevision -eq 0) { $BuildRevision = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
if ($BuildRevision -lt 1) { throw 'BuildRevision must be a positive increasing release number' }
$outputPath = [IO.Path]::GetFullPath($Output)
$build = Join-Path ([IO.Path]::GetDirectoryName($outputPath)) ('msi-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $build -Force | Out-Null
& (Join-Path $PSScriptRoot 'InstallerUI/build.ps1') -OutputDirectory (Join-Path $build 'ui')
Copy-Item -LiteralPath $exe -Destination (Join-Path $build 'AppExe')
& "$env:SystemRoot/System32/makecab.exe" /D CompressionType=MSZIP (Join-Path $build 'AppExe') (Join-Path $build 'app.cab') | Out-Null
if ($LASTEXITCODE) { throw 'Cabinet creation failed' }
if (Test-Path -LiteralPath $outputPath) { throw 'Output file already exists' }
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($outputPath, 3)
# Set the database encoding before inserting localized strings.
[IO.File]::WriteAllText((Join-Path $build '_ForceCodepage.idt'), "`r`n`r`n936`t_ForceCodepage`r`n", [Text.Encoding]::ASCII)
$db.Import($build, '_ForceCodepage.idt')
 $view = $db.OpenView('CREATE TABLE `MsiEmbeddedUI` (`MsiEmbeddedUI` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, `Attributes` SHORT NOT NULL, `MessageFilter` LONG, `Data` OBJECT NOT NULL PRIMARY KEY `MsiEmbeddedUI`)')
 $view.Execute(); $view.Close()
 $view = $db.OpenView('INSERT INTO `MsiEmbeddedUI` (`MsiEmbeddedUI`,`FileName`,`Attributes`,`MessageFilter`,`Data`) VALUES (?,?,?,?,?)')
 $record = $installer.CreateRecord(5)
 $record.StringData(1) = 'ModernInstallerUI'
 $record.StringData(2) = 'ModernUI.dll'
 $record.IntegerData(3) = 3
 $record.IntegerData(4) = 0x0E003FFF
 $record.SetStream(5, (Join-Path $build 'ui/ModernUI.dll'))
 $view.Execute($record); $view.Close()
function Sql([string]$query) {
    $view = $db.OpenView($query)
    try { $view.Execute() } finally { $view.Close() }
}
function Row([string]$table, [string[]]$columns, [object[]]$values) {
    $fields = ($columns | ForEach-Object { '`' + $_ + '`' }) -join ','
    $params = ($values | ForEach-Object { '?' }) -join ','
    $view = $db.OpenView(('INSERT INTO `{0}` ({1}) VALUES ({2})' -f $table,$fields,$params))
    $record = $installer.CreateRecord($values.Count)
    for ($i = 0; $i -lt $values.Count; $i++) {
        if ($null -eq $values[$i]) { continue }
        if ($values[$i] -is [int]) { $record.IntegerData($i + 1) = $values[$i] }
        else { $record.StringData($i + 1) = [string]$values[$i] }
    }
    try { $view.Execute($record) } finally { $view.Close() }
}
Sql 'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` CHAR(0) NOT NULL LOCALIZABLE PRIMARY KEY `Property`)'
foreach ($pair in @{
    ProductCode=$productCode; UpgradeCode='{9C41FA29-C439-461B-9F2B-1DC01F9304E8}';
    ProductName='学习通作业看板'; ProductVersion=$version; ProductLanguage='2052'; Manufacturer='CoolLiangYu';
    INSTALLER_BUILD=$BuildRevision.ToString([Globalization.CultureInfo]::InvariantCulture);
    ALLUSERS='1'; INSTALLLEVEL='1'; ARPPRODUCTICON='AppIcon'; ARPNOMODIFY='1';
    ARPURLINFOABOUT='https://github.com/Coolliangyuu/chaoxingTasksPanel'; SecureCustomProperties='OLDPRODUCTS;NEWERPRODUCTS;INSTALLDIR;MODERN_UI_CANCELED;DELETEUSERDATA;DATACHOICE_MADE';
    REINSTALLMODE='amus'; DefaultUIFont='NormalFont'
}.GetEnumerator()) { Row Property @('Property','Value') @($pair.Key,$pair.Value) }
Sql 'CREATE TABLE `TextStyle` (`TextStyle` CHAR(72) NOT NULL, `FaceName` CHAR(32) NOT NULL, `Size` SHORT NOT NULL, `Color` LONG, `StyleBits` SHORT PRIMARY KEY `TextStyle`)'
Row TextStyle @('TextStyle','FaceName','Size','StyleBits') @('NormalFont','Microsoft YaHei UI',9,0)
Sql 'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Directory`)'
foreach ($dir in @(
    @('TARGETDIR',$null,'SourceDir'), @('ProgramFiles64Folder','TARGETDIR','.'), @('INSTALLDIR','ProgramFiles64Folder','chaoxingTasksPanel'),
    @('DesktopFolder','TARGETDIR','.'), @('ProgramMenuFolder','TARGETDIR','.'), @('AppMenu','ProgramMenuFolder','TASKPANE|学习通作业看板')
)) { Row Directory @('Directory','Directory_Parent','DefaultDir') $dir }
Sql 'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)'
Row Component @('Component','ComponentId','Directory_','Attributes','KeyPath') @('MainComponent','{BA5517D0-EF22-49CC-B101-E00F0D080151}','INSTALLDIR',256,'AppExe')
Sql 'CREATE TABLE `Registry` (`Registry` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL LOCALIZABLE, `Name` CHAR(255) LOCALIZABLE, `Value` CHAR(0) LOCALIZABLE, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Registry`)'
Row Registry @('Registry','Root','Key','Name','Value','Component_') @('SavedInstallDir',2,'Software\CoolLiangYu\chaoxingTasksPanel','InstallDir','[INSTALLDIR]','MainComponent')
Sql 'CREATE TABLE `RegLocator` (`Signature_` CHAR(72) NOT NULL, `Root` SHORT NOT NULL, `Key` CHAR(255) NOT NULL, `Name` CHAR(255), `Type` SHORT PRIMARY KEY `Signature_`)'
Row RegLocator @('Signature_','Root','Key','Name','Type') @('PreviousInstallDir',2,'Software\CoolLiangYu\chaoxingTasksPanel','InstallDir',18)
Sql 'CREATE TABLE `AppSearch` (`Property` CHAR(72) NOT NULL, `Signature_` CHAR(72) NOT NULL PRIMARY KEY `Property`, `Signature_`)'
Sql 'CREATE TABLE `Signature` (`Signature` CHAR(72) NOT NULL, `FileName` CHAR(255), `MinVersion` CHAR(20), `MaxVersion` CHAR(20), `MinSize` LONG, `MaxSize` LONG, `MinDate` LONG, `MaxDate` LONG, `Languages` CHAR(255) PRIMARY KEY `Signature`)'
Row AppSearch @('Property','Signature_') @('INSTALLDIR','PreviousInstallDir')
Sql 'CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL LOCALIZABLE, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` LONG NOT NULL PRIMARY KEY `File`)'
Row File @('File','Component_','FileName','FileSize','Version','Attributes','Sequence') @('AppExe','MainComponent','TASKPANE.EXE|chaoxingTaskPanel.exe',[int](Get-Item -LiteralPath $exe).Length,($version+'.0'),16384,1)
Sql 'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64) LOCALIZABLE, `Description` CHAR(255) LOCALIZABLE, `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)'
Row Feature @('Feature','Title','Display','Level','Directory_','Attributes') @('Main','chaoxingTasksPanel',1,1,'INSTALLDIR',0)
Sql 'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)'
Row FeatureComponents @('Feature_','Component_') @('Main','MainComponent')
Sql 'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` LONG NOT NULL, `DiskPrompt` CHAR(64) LOCALIZABLE, `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)'
Row Media @('DiskId','LastSequence','Cabinet') @(1,1,'#app.cab')
Sql 'CREATE TABLE `Icon` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)'
$view = $db.OpenView('INSERT INTO `Icon` (`Name`,`Data`) VALUES (?,?)')
$record = $installer.CreateRecord(2)
$record.StringData(1) = 'AppIcon'
$record.SetStream(2, (Join-Path $PSScriptRoot 'StudyTasks/StudyTasks.ico'))
$view.Execute($record)
$view.Close()
$view = $db.OpenView('INSERT INTO `_Streams` (`Name`,`Data`) VALUES (?,?)')
$record = $installer.CreateRecord(2)
$record.StringData(1) = 'app.cab'
$record.SetStream(2, (Join-Path $build 'app.cab'))
$view.Execute($record)
$view.Close()
Sql 'CREATE TABLE `Shortcut` (`Shortcut` CHAR(72) NOT NULL, `Directory_` CHAR(72) NOT NULL, `Name` CHAR(128) NOT NULL LOCALIZABLE, `Component_` CHAR(72) NOT NULL, `Target` CHAR(255) NOT NULL, `Arguments` CHAR(255), `Description` CHAR(255) LOCALIZABLE, `Hotkey` SHORT, `Icon_` CHAR(72), `IconIndex` SHORT, `ShowCmd` SHORT, `WkDir` CHAR(72) PRIMARY KEY `Shortcut`)'
foreach ($shortcut in @(@('DesktopShortcut','DesktopFolder'),@('MenuShortcut','AppMenu'))) {
    Row Shortcut @('Shortcut','Directory_','Name','Component_','Target','Description','Icon_','IconIndex','ShowCmd','WkDir') @($shortcut[0],$shortcut[1],'TASKPANE|学习通作业看板','MainComponent','[#AppExe]','学习通作业看板','AppIcon',0,1,'INSTALLDIR')
}
Sql 'CREATE TABLE `MsiShortcutProperty` (`MsiShortcutProperty` CHAR(72) NOT NULL, `Shortcut_` CHAR(72) NOT NULL, `PropertyKey` CHAR(255) NOT NULL, `PropVariantValue` CHAR(255) NOT NULL PRIMARY KEY `MsiShortcutProperty`)'
Row MsiShortcutProperty @('MsiShortcutProperty','Shortcut_','PropertyKey','PropVariantValue') @('MenuAppId','MenuShortcut','System.AppUserModel.ID','CoolLiangYu.chaoxingTaskPanel')
Sql 'CREATE TABLE `RemoveFile` (`FileKey` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) LOCALIZABLE, `DirProperty` CHAR(72) NOT NULL, `InstallMode` SHORT NOT NULL PRIMARY KEY `FileKey`)'
Row RemoveFile @('FileKey','Component_','DirProperty','InstallMode') @('RemoveMenuFolder','MainComponent','AppMenu',2)
Sql 'CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` CHAR(255) PRIMARY KEY `Action`)'
Row CustomAction @('Action','Type','Source','Target') @('CleanupStartup',1537,'ModernUIActions','CommitUninstallCleanup')
Row CustomAction @('Action','Type','Source','Target') @('PrepareUninstallCleanup',1,'ModernUIActions','PrepareUninstallCleanup')
Row CustomAction @('Action','Type','Source','Target') @('PromptUninstallData',1,'ModernUIActions','PromptUninstallData')
Row CustomAction @('Action','Type','Source','Target') @('SetInstallLocation',51,'ARPINSTALLLOCATION','[INSTALLDIR]')
Sql 'CREATE TABLE `Binary` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)'
$view = $db.OpenView('INSERT INTO `Binary` (`Name`,`Data`) VALUES (?,?)')
$record = $installer.CreateRecord(2)
$record.StringData(1) = 'ModernUIActions'
$record.SetStream(2, (Join-Path $build 'ui/ModernUI.dll'))
$view.Execute($record); $view.Close()
Row CustomAction @('Action','Type','Source','Target') @('CancelModernUI',1,'ModernUIActions','CancelBeforeInstall')
Row CustomAction @('Action','Type','Source','Target') @('CheckWindowsSupport',1,'ModernUIActions','CheckWindowsSupport')
Row CustomAction @('Action','Type','Source','Target') @('CheckUpgradeOrder',1,'ModernUIActions','CheckUpgradeOrder')
Sql 'CREATE TABLE `Upgrade` (`UpgradeCode` CHAR(38) NOT NULL, `VersionMin` CHAR(20), `VersionMax` CHAR(20), `Language` CHAR(255), `Attributes` LONG NOT NULL, `Remove` CHAR(255), `ActionProperty` CHAR(72) NOT NULL PRIMARY KEY `UpgradeCode`, `VersionMin`, `VersionMax`, `Language`, `Attributes`)'
Row Upgrade @('UpgradeCode','VersionMax','Attributes','ActionProperty') @('{9C41FA29-C439-461B-9F2B-1DC01F9304E8}',$version,513,'OLDPRODUCTS')
Row Upgrade @('UpgradeCode','VersionMin','Attributes','ActionProperty') @('{9C41FA29-C439-461B-9F2B-1DC01F9304E8}',$version,2,'NEWERPRODUCTS')
Sql 'CREATE TABLE `LaunchCondition` (`Condition` CHAR(255) NOT NULL, `Description` CHAR(255) NOT NULL LOCALIZABLE PRIMARY KEY `Condition`)'
Row LaunchCondition @('Condition','Description') @('VersionNT64','此应用需要 64 位 Windows 系统。')
Row LaunchCondition @('Condition','Description') @('OS_COMPATIBLE=1 OR (Installed AND REMOVE="ALL")','此版本需要 Windows 10 22H2（内部版本 19045）或更新的 64 位 Windows，包含 Windows 11。当前系统不满足要求，无法安装。')
Row LaunchCondition @('Condition','Description') @('Installed OR NOT NEWERPRODUCTS','已安装更新版本，请使用现有程序或更新的安装包。')
Row LaunchCondition @('Condition','Description') @('BUILD_COMPATIBLE=1 OR Installed OR REMOVE="ALL"','已安装更新的修订版本，请使用当前程序或更新的安装包。')
foreach ($table in @('InstallExecuteSequence','InstallUISequence','AdminExecuteSequence','AdminUISequence')) {
    Sql ('CREATE TABLE `{0}` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)' -f $table)
}
foreach ($action in @(
    @('FindRelatedProducts',25),@('LaunchConditions',100),@('ValidateProductID',700),
    @('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('MigrateFeatureStates',1200),
    @('InstallValidate',1400),@('InstallInitialize',1500),@('RemoveExistingProducts',1550),
    @('ProcessComponents',1600),@('UnpublishFeatures',1800),@('RemoveShortcuts',3200),@('RemoveFiles',3500),
    @('RemoveFolders',3600),@('CreateFolders',3700),@('InstallFiles',4000),@('CreateShortcuts',4500),
    @('RemoveRegistryValues',2600),@('WriteRegistryValues',5000),@('RegisterUser',6000),@('RegisterProduct',6100),@('PublishFeatures',6300),@('PublishProduct',6400),@('InstallFinalize',6600)
)) { Row InstallExecuteSequence @('Action','Sequence') $action }
Sql 'UPDATE `InstallExecuteSequence` SET `Condition`=''OLDPRODUCTS'' WHERE `Action`=''RemoveExistingProducts'''
foreach ($action in @(@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('InstallValidate',1400),@('InstallInitialize',1500),@('InstallAdminPackage',3900),@('InstallFiles',4000),@('InstallFinalize',6600))) {
    Row AdminExecuteSequence @('Action','Sequence') $action
}
foreach ($action in @(@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('ExecuteAction',1300))) {
    Row AdminUISequence @('Action','Sequence') $action
}
Sql 'CREATE TABLE `Dialog` (`Dialog` CHAR(72) NOT NULL, `HCentering` SHORT NOT NULL, `VCentering` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Title` CHAR(128) LOCALIZABLE, `Control_First` CHAR(50) NOT NULL, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50) PRIMARY KEY `Dialog`)'
Sql 'CREATE TABLE `Control` (`Dialog_` CHAR(72) NOT NULL, `Control` CHAR(50) NOT NULL, `Type` CHAR(20) NOT NULL, `X` SHORT NOT NULL, `Y` SHORT NOT NULL, `Width` SHORT NOT NULL, `Height` SHORT NOT NULL, `Attributes` LONG, `Property` CHAR(72), `Text` CHAR(0) LOCALIZABLE, `Control_Next` CHAR(50), `Help` CHAR(50) LOCALIZABLE PRIMARY KEY `Dialog_`, `Control`)'
Sql 'CREATE TABLE `ControlEvent` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Argument` CHAR(255) NOT NULL, `Condition` CHAR(255), `Ordering` SHORT PRIMARY KEY `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`)'
. (Join-Path $PSScriptRoot 'installer-ui.ps1')
Row InstallUISequence @('Action','Condition','Sequence') @('PromptUninstallData','REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE AND NOT DATACHOICE_MADE',1260)
Row InstallExecuteSequence @('Action','Condition','Sequence') @('PromptUninstallData','REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE AND NOT DATACHOICE_MADE',1380)
Row InstallExecuteSequence @('Action','Condition','Sequence') @('PrepareUninstallCleanup','REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE',6520)
Row InstallExecuteSequence @('Action','Condition','Sequence') @('CleanupStartup','REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE',6550)
Row InstallExecuteSequence @('Action','Sequence') @('SetInstallLocation',1050)
Row InstallExecuteSequence @('Action','Condition','Sequence') @('CancelModernUI','MODERN_UI_CANCELED=1',1)
Row InstallUISequence @('Action','Condition','Sequence') @('CancelModernUI','MODERN_UI_CANCELED=1',1)
foreach($sequence in @('InstallUISequence','InstallExecuteSequence')) {
    Row $sequence @('Action','Sequence') @('CheckWindowsSupport',20)
    Row $sequence @('Action','Sequence') @('CheckUpgradeOrder',60)
}
$summary = $db.SummaryInformation(20)
$summary.Property(1) = 936
$summary.Property(2) = '学习通作业看板安装包'
$summary.Property(3) = '学习通作业看板 ' + $version
$summary.Property(4) = 'CoolLiangYu'
$summary.Property(7) = 'x64;2052'
$summary.Property(9) = '{' + [Guid]::NewGuid().ToString().ToUpperInvariant() + '}'
$summary.Property(14) = 500
$summary.Property(15) = 2
$summary.Persist()
$db.Commit()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
Write-Output $outputPath


