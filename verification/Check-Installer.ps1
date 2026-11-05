param([Parameter(Mandatory)][string]$Installer, [Parameter(Mandatory)][string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
$msi = (Resolve-Path -LiteralPath $Installer).Path
$root = [IO.Path]::GetFullPath($WorkDirectory)
New-Item -ItemType Directory -Force $root | Out-Null
$engine = New-Object -ComObject WindowsInstaller.Installer
$engine.UILevel = 2
[void]$engine.EnableLog('voicewarmup', (Join-Path $root 'path-check.log'))
$db = $engine.OpenDatabase($msi, 0)
function Rows([string]$sql,[int]$columns) {
    $v=$db.OpenView($sql); [void]$v.Execute()
    $results=@()
    while($r=$v.Fetch()) {
        $values=@(); for($n=1;$n -le $columns;$n++) { $values += $r.StringData($n) }
        $results += ,$values
    }
    [void]$v.Close()
    return ,$results
}
function Assert([bool]$ok,[string]$name) { if(-not $ok) {throw $name}; Write-Output "PASS: $name" }
$embedded=Rows 'SELECT `FileName`,`Attributes`,`MessageFilter` FROM `MsiEmbeddedUI`' 3
Assert ($embedded.Count -eq 1 -and $embedded[0][0] -eq 'ModernUI.dll' -and $embedded[0][1] -eq '3') 'Modern native UI handles full setup and basic Settings removal'
$cancel=Rows 'SELECT `Condition`,`Sequence` FROM `InstallUISequence` WHERE `Action`=''CancelModernUI''' 2
Assert ($cancel[0][0] -eq 'MODERN_UI_CANCELED=1' -and $cancel[0][1] -eq '1') 'Cancel exits in the client before execute/elevation'
foreach($sequence in @('InstallUISequence','InstallExecuteSequence')) {
    $check=Rows ('SELECT `Sequence` FROM `{0}` WHERE `Action`=''CheckWindowsSupport''' -f $sequence) 1
    Assert ($check[0][0] -eq '20') ("System compatibility checked before installation in $sequence")
    $order=Rows ('SELECT `Sequence` FROM `{0}` WHERE `Action`=''CheckUpgradeOrder''' -f $sequence) 1
    Assert ($order[0][0] -eq '60') ("Installer revision order checked in $sequence")
}
$product=Rows 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode''' 1
Assert ($product[0][0] -ne '{9F7D1600-03D8-4EB0-A1E7-3D49815C1500}') 'New release does not reuse the legacy ProductCode'
$upgrades=Rows 'SELECT `VersionMin`,`VersionMax`,`Attributes`,`ActionProperty` FROM `Upgrade`' 4
Assert (($upgrades | Where-Object {$_[3] -eq 'OLDPRODUCTS' -and ([int]$_[2] -band 512)}).Count -gt 0) 'Upgrade includes older revisions with the same public version'
$initialize=Rows 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''InstallInitialize''' 1
$remove=Rows 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''RemoveExistingProducts''' 1
$components=Rows 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''ProcessComponents''' 1
Assert ([int]$initialize[0][0] -lt [int]$remove[0][0] -and [int]$remove[0][0] -lt [int]$components[0][0]) 'Old product removal is inside rollback transaction before file operations'
$cleanup=Rows 'SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action`=''CleanupStartup''' 1
Assert ($cleanup[0][0] -eq 'REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE') 'Upgrade preserves the existing autostart preference'
$commit=Rows 'SELECT `Type`,`Source`,`Target` FROM `CustomAction` WHERE `Action`=''CleanupStartup''' 3
Assert ($commit[0][0] -eq '1537' -and $commit[0][1] -eq 'ModernUIActions' -and $commit[0][2] -eq 'CommitUninstallCleanup') 'User cleanup runs natively on successful commit, impersonating the uninstalling user'
$prepare=Rows 'SELECT `Sequence`,`Condition` FROM `InstallExecuteSequence` WHERE `Action`=''PrepareUninstallCleanup''' 2
$commitSequence=Rows 'SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''CleanupStartup''' 1
Assert ($prepare[0][1] -eq 'REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE' -and [int]$prepare[0][0] -lt [int]$commitSequence[0][0] -and [int]$commitSequence[0][0] -lt 6600) 'Cleanup is prepared within the transaction, scheduled before InstallFinalize, and skipped on upgrade'
foreach($sequence in @('InstallUISequence','InstallExecuteSequence')) {
    $prompt=Rows ('SELECT `Condition` FROM `{0}` WHERE `Action`=''PromptUninstallData''' -f $sequence) 1
    Assert ($prompt[0][0] -eq 'REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE AND NOT DATACHOICE_MADE') ("Uninstall data fallback exists without duplicate prompting in $sequence")
}
$secure=Rows 'SELECT `Value` FROM `Property` WHERE `Property`=''SecureCustomProperties''' 1
Assert (($secure[0][0] -split ';') -contains 'DELETEUSERDATA' -and ($secure[0][0] -split ';') -contains 'DATACHOICE_MADE') 'Data choice crosses the client/server boundary'
$dialogs=Rows 'SELECT `Dialog`,`Control_First`,`Control_Default`,`Control_Cancel` FROM `Dialog`' 4
$controls=Rows 'SELECT `Dialog_`,`Control`,`Control_Next`,`Type` FROM `Control`' 4
foreach($dialog in $dialogs) {
    $ids=@($controls | Where-Object {$_[0] -eq $dialog[0]} | ForEach-Object {$_[1]})
    foreach($id in $dialog[1..3]) { if($id -and $id -notin $ids){throw "Invalid dialog control: $($dialog[0]).$id"} }
}
foreach($control in $controls) {
    if($control[2] -and -not ($controls | Where-Object {$_[0] -eq $control[0] -and $_[1] -eq $control[2]})) {
        throw "Invalid tab order: $($control[0]).$($control[1])"
    }
}
foreach($dialog in $dialogs) {
    $linked=@($controls | Where-Object {$_[0] -eq $dialog[0] -and $_[2]})
    $next=@{}
    $incoming=@{}
    foreach($control in $linked) {
        $next[$control[1]]=$control[2]
        if($incoming.ContainsKey($control[2])) {throw "Invalid tab cycle (2810): $($dialog[0]), duplicate predecessor for $($control[2])"}
        $incoming[$control[2]]=$control[1]
    }
    $visited=[Collections.Generic.HashSet[string]]::new()
    $current=$dialog[1]
    while($visited.Add($current)) {
        if(-not $next.ContainsKey($current)){throw "Broken tab cycle (2809): $($dialog[0]).$current"}
        $current=$next[$current]
    }
    if($current -ne $dialog[1] -or $visited.Count -ne $linked.Count){throw "Disconnected tab cycle: $($dialog[0])"}
}
Assert ($true) 'Every dialog has one complete tab cycle without duplicate predecessors'
Assert ($dialogs.Count -eq 9) 'Wizard dialog and tab references valid'
foreach($edge in @(@('WelcomeDlg','InstallDirDlg'),@('InstallDirDlg','ReadyDlg'),@('ReadyDlg','InstallDirDlg'))) {
    $links=Rows ('SELECT `Argument` FROM `ControlEvent` WHERE `Dialog_`=''{0}'' AND `Event`=''NewDialog'' AND `Argument`=''{1}''' -f $edge[0],$edge[1]) 1
    Assert ($links.Count -eq 1) ("Wizard navigation {0} -> {1}" -f $edge[0],$edge[1])
}
$bindings=Rows 'SELECT `Argument` FROM `ControlEvent` WHERE `Event`=''SetTargetPath''' 1
Assert ($bindings.Count -eq 2 -and ($bindings | Where-Object {$_[0] -ne 'INSTALLDIR'}).Count -eq 0) 'Edited and browsed paths update the MSI target directory'
$mapping=Rows 'SELECT `Attribute` FROM `EventMapping` WHERE `Event`=''SetProgress''' 1
Assert ($mapping.Count -eq 1 -and $mapping[0][0] -eq 'Progress') 'Real MSI progress events connected'
$saved=Rows 'SELECT `Value`,`Component_` FROM `Registry` WHERE `Registry`=''SavedInstallDir''' 2
Assert ($saved[0][0] -eq '[INSTALLDIR]') 'Installation writes selected directory to registry'
$search=Rows 'SELECT `Condition`,`Sequence` FROM `InstallExecuteSequence` WHERE `Action`=''AppSearch''' 2
Assert ($search[0][0] -eq 'NOT INSTALLDIR' -and [int]$search[0][1] -lt 800) 'Upgrade reads saved location before costing; explicit paths take priority'
$keys=Rows 'SELECT `Signature_`,`Root`,`Key`,`Name`,`Type` FROM `RegLocator`' 5
Assert ($keys[0][1] -eq '2' -and $keys[0][4] -eq '18') 'Upgrade searches machine-wide 64-bit installation record'
$shortcuts=Rows 'SELECT `Target`,`WkDir` FROM `Shortcut`' 2
Assert (($shortcuts | Where-Object {$_[0] -ne '[#AppExe]' -or $_[1] -ne 'INSTALLDIR'}).Count -eq 0) 'Shortcuts follow the actual installed executable'
$names=Rows 'SELECT `Shortcut`,`Directory_`,`Name` FROM `Shortcut`' 3
Assert ($names.Count -eq 2 -and ($names | Where-Object {($_[2] -split '\|')[-1] -ne '学习通作业看板'}).Count -eq 0) 'Desktop and Start Menu shortcuts both have the Chinese display name'
$shortcutLocations=@{}
foreach($entry in $names){$shortcutLocations[$entry[0]]=$entry[1]}
Assert ($shortcutLocations.Count -eq 2 -and $shortcutLocations['DesktopShortcut'] -eq 'DesktopFolder' -and $shortcutLocations['MenuShortcut'] -eq 'AppMenu') 'Installer creates exactly one desktop and one Start Menu shortcut'
$menuDirectory=Rows 'SELECT `DefaultDir` FROM `Directory` WHERE `Directory`=''AppMenu''' 1
Assert (($menuDirectory[0][0] -split '\|')[-1] -eq '学习通作业看板') 'Start Menu folder uses the Chinese name'
$appId=Rows 'SELECT `Shortcut_`,`PropertyKey`,`PropVariantValue` FROM `MsiShortcutProperty`' 3
Assert ($appId.Count -eq 1 -and $appId[0][0] -eq 'MenuShortcut' -and $appId[0][1] -eq 'System.AppUserModel.ID' -and $appId[0][2] -eq 'CoolLiangYu.chaoxingTaskPanel') 'Chinese Start Menu shortcut retains notification identity'
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)

# Use a disposable copy and a private HKCU fixture, never the real product's registry entry.
$fixture=Join-Path $root 'path-search-fixture.msi'
Copy-Item -LiteralPath $msi -Destination $fixture
$fixtureDb=$engine.OpenDatabase($fixture,1)
$key='Software\CodexInstallerTests\'+[Guid]::NewGuid().ToString('N')
$view=$fixtureDb.OpenView(('UPDATE `RegLocator` SET `Root`=1,`Key`=''{0}''' -f $key))
[void]$view.Execute(); [void]$view.Close(); [void]$fixtureDb.Commit()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
$fixtureProduct='{'+[Guid]::NewGuid().ToString().ToUpperInvariant()+'}'
$view=$fixtureDb.OpenView(('UPDATE `Property` SET `Value`=''{0}'' WHERE `Property`=''ProductCode''' -f $fixtureProduct))
[void]$view.Execute();[void]$view.Close();[void]$fixtureDb.Commit()
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($fixtureDb)
$expected=Join-Path $root '测试 安装位置'
New-Item -ItemType Directory -Force $expected | Out-Null
$reg=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($key)
try {
    $reg.SetValue('InstallDir',$expected,[Microsoft.Win32.RegistryValueKind]::String)
    $session=$engine.OpenPackage($fixture,0)
    $session.Property('INSTALLDIR')=''
    $status=$session.DoAction('AppSearch')
    Write-Output "AppSearch status: $status; restored property: $($session.Property('INSTALLDIR'))"
    if($status -ne 1) { Get-Content -LiteralPath (Join-Path $root 'path-check.log') -Tail 25 | Write-Output }
    Assert ($status -eq 1) 'MSI AppSearch action succeeds'
    Assert ($session.Property('INSTALLDIR').TrimEnd('\') -eq $expected.TrimEnd('\')) 'MSI engine restores a saved path containing Chinese and spaces'
    foreach($action in @('CostInitialize','FileCost','CostFinalize')) {
        Assert ($session.DoAction($action) -eq 1) ("MSI $action action succeeds")
    }
    Assert ($session.TargetPath('INSTALLDIR').TrimEnd('\') -eq $expected.TrimEnd('\')) 'Resolved file target retains the custom path'
    Assert ($session.TargetPath('AppMenu').TrimEnd('\').EndsWith('\学习通作业看板')) 'MSI engine resolves the Chinese Start Menu directory'
    $checkStatus=$session.DoAction('CheckWindowsSupport')
    Write-Output "MSI native check: status=$checkStatus; OS=$($session.Property('OS_DETECTED')); compatible=$($session.Property('OS_COMPATIBLE'))"
    Assert ($checkStatus -eq 1 -and $session.Property('OS_COMPATIBLE') -eq '1') 'Native compatibility action accepts the current supported host'
    Assert ($session.DoAction('CheckUpgradeOrder') -eq 1 -and $session.Property('BUILD_COMPATIBLE') -eq '1') 'Fresh and legacy installations pass revision checks'
    $session.Property('OS_COMPATIBLE')=''
    $session.Property('Installed')=''
    $session.Property('REMOVE')=''
    Assert ($session.DoAction('LaunchConditions') -eq 3) 'Unsupported systems cannot pass the MSI launch condition'
    $session.Property('OS_COMPATIBLE')='1'
    Assert ($session.DoAction('LaunchConditions') -eq 1) 'Supported systems pass the MSI launch condition'
    $session.Property('OS_COMPATIBLE')=''
    $session.Property('Installed')='1'
    $session.Property('REMOVE')='ALL'
    Assert ($session.DoAction('LaunchConditions') -eq 1) 'Removing an existing installation remains possible'
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session)
}
finally {
    $reg.Dispose()
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($key,$false)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($engine)
}
