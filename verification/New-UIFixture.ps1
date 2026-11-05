param([Parameter(Mandatory)][string]$Installer,[Parameter(Mandatory)][string]$Output,
    [ValidateSet('success','cancel','failure')][string]$Outcome='success')
$ErrorActionPreference='Stop'
Copy-Item -LiteralPath (Resolve-Path $Installer).Path -Destination $Output
$engine=New-Object -ComObject WindowsInstaller.Installer
$db=$engine.OpenDatabase([IO.Path]::GetFullPath($Output),1)
function Sql([string]$query) {$v=$db.OpenView($query);[void]$v.Execute();[void]$v.Close();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($v)}
$code='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
Sql ('UPDATE `Property` SET `Value`=''{0}'' WHERE `Property`=''ProductCode''' -f $code)
Sql ('UPDATE `Property` SET `Value`=''{0}'' WHERE `Property`=''UpgradeCode''' -f ('{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'))
Sql 'DELETE FROM `Property` WHERE `Property`=''ALLUSERS'''
$summary=$db.SummaryInformation(20)
$summary.Property(15)=10 # Compressed and explicitly non-elevated (LUA) test package.
$summary.Property(9)='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
[void]$summary.Persist()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary)
# No copying files, registry writes, shortcuts, registration, upgrades or removal.
# Keep a real empty transaction to exercise MSI progress/completion callbacks.
Sql 'DELETE FROM `InstallExecuteSequence`'
foreach($action in @(@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('InstallInitialize',1500),@('InstallFinalize',6600))) {
    Sql ('INSERT INTO `InstallExecuteSequence` (`Action`,`Sequence`) VALUES (''{0}'',{1})' -f $action[0],$action[1])
}
if($Outcome -eq 'cancel') {
    Sql 'INSERT INTO `InstallExecuteSequence` (`Action`,`Sequence`) VALUES (''CancelModernUI'',1)'
}
if($Outcome -eq 'failure') {
    Sql 'INSERT INTO `InstallExecuteSequence` (`Action`,`Sequence`) VALUES (''LaunchConditions'',100)'
    Sql 'INSERT INTO `LaunchCondition` (`Condition`,`Description`) VALUES (''0'',''Intentional UI engine test failure'')'
}
[void]$db.Commit();[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine)
Write-Output "UI-only MSI fixture: $([IO.Path]::GetFullPath($Output))"
Write-Output "ProductCode (must remain unregistered): $code"
