# Native Windows Installer dialogs; loaded into build-installer.ps1's scope.
function Dialog([string]$id, [string]$title, [int]$attributes = 3, [string]$first = 'Next') {
    $default = if ($id -eq 'ProgressDlg') { 'Cancel' } else { 'Next' }
    Row Dialog @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @($id,50,50,400,270,$attributes,$title,$first,$default,'Cancel')
}
function Control([string]$dialog,[string]$id,[string]$type,[int]$x,[int]$y,[int]$width,[int]$height,[string]$text='',[string]$property='',[string]$next='',[int]$attributes=3) {
    Row Control @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Property','Text','Control_Next') @($dialog,$id,$type,$x,$y,$width,$height,$attributes,$property,$text,$next)
}
function Event([string]$dialog,[string]$control,[string]$event,[string]$argument,[int]$order=1,[string]$condition='1') {
    Row ControlEvent @('Dialog_','Control_','Event','Argument','Condition','Ordering') @($dialog,$control,$event,$argument,$condition,$order)
}
function Frame([string]$dialog,[string]$heading,[string]$body) {
    Control $dialog Heading Text 20 15 360 25 ('{\HeadingFont}' + $heading)
    Control $dialog HeaderLine Line 0 48 400 0
    Control $dialog Body Text 22 66 355 65 $body
    Control $dialog FooterLine Line 0 229 400 0
    Control $dialog Cancel PushButton 310 243 70 19 '取消' '' 'Next'
    Event $dialog Cancel EndDialog Exit
}
Row TextStyle @('TextStyle','FaceName','Size','StyleBits') @('HeadingFont','Microsoft YaHei UI',12,1)

Dialog WelcomeDlg '学习通作业看板 · 欢迎安装'
Frame WelcomeDlg '安装或升级学习通作业看板' '此向导将安装或升级学习通作业看板 v[ProductVersion]。检测到旧版时将自动替换程序，保留个人数据及原安装位置。'
Control WelcomeDlg Next PushButton 230 243 70 19 '下一步 >' '' Cancel
Event WelcomeDlg Next NewDialog InstallDirDlg

Dialog InstallDirDlg '学习通作业看板 · 安装位置' 3 Path
Frame InstallDirDlg '选择安装位置' '您可以使用默认位置，直接修改下方路径，或点击「浏览」选择其他文件夹。升级时将自动读取之前记录的安装位置。'
Sql 'UPDATE `Control` SET `Control_Next`=''Path'' WHERE `Dialog_`=''InstallDirDlg'' AND `Control`=''Cancel'''
Control InstallDirDlg Path PathEdit 22 137 275 20 '' INSTALLDIR Browse
Control InstallDirDlg Browse PushButton 307 137 70 20 '浏览…' '' Back
Control InstallDirDlg Note Text 22 169 355 35 '默认安装到 Program Files 下的 chaoxingTasksPanel 文件夹。请为本应用选择专用文件夹。'
Control InstallDirDlg Back PushButton 150 243 70 19 '< 上一步' '' Next
Control InstallDirDlg Next PushButton 230 243 70 19 '下一步 >' '' Cancel
Event InstallDirDlg Back NewDialog WelcomeDlg
Event InstallDirDlg Browse '[BROWSEPATH]' '[INSTALLDIR]' 1
Event InstallDirDlg Browse SpawnDialog BrowseDlg 2
Event InstallDirDlg Next SetTargetPath INSTALLDIR 1
Event InstallDirDlg Next NewDialog ReadyDlg 2

Dialog BrowseDlg '选择安装文件夹' 3 Folders
Control BrowseDlg Label Text 20 15 360 18 '选择文件夹，也可以在下方直接输入完整路径。'
Control BrowseDlg Drives DirectoryCombo 20 39 280 70 '' BROWSEPATH Up 458755
Control BrowseDlg Up PushButton 310 39 70 20 '上一级' '' NewFolder
Control BrowseDlg Folders DirectoryList 20 68 360 123 '' BROWSEPATH Path
Control BrowseDlg NewFolder PushButton 280 198 100 19 '新建文件夹' '' Folders
Control BrowseDlg Path PathEdit 20 198 250 20 '' BROWSEPATH Next
Control BrowseDlg Next PushButton 230 243 70 19 '确定' '' Cancel
Control BrowseDlg Cancel PushButton 310 243 70 19 '取消' '' Drives
Event BrowseDlg Up DirectoryListUp 0
Event BrowseDlg NewFolder DirectoryListNew 0
Event BrowseDlg Next '[INSTALLDIR]' '[BROWSEPATH]' 1
Event BrowseDlg Next SetTargetPath INSTALLDIR 2
Event BrowseDlg Next EndDialog Return 3
Event BrowseDlg Cancel EndDialog Return

Dialog ReadyDlg '学习通作业看板 · 准备安装'
Frame ReadyDlg '准备安装' '点击「安装」开始复制文件并创建快捷方式。如需修改安装位置，请点击「上一步」。'
Sql 'UPDATE `Control` SET `Control_Next`=''Back'' WHERE `Dialog_`=''ReadyDlg'' AND `Control`=''Cancel'''
Control ReadyDlg Location Text 22 133 355 65 "安装位置：`r`n[INSTALLDIR]"
Control ReadyDlg Back PushButton 150 243 70 19 '< 上一步' '' Next
Control ReadyDlg Next PushButton 230 243 70 19 '安装' '' Cancel
Event ReadyDlg Back NewDialog InstallDirDlg
Event ReadyDlg Next EndDialog Return

Dialog MaintenanceDlg '学习通作业看板 · 维护'
Frame MaintenanceDlg '维护已安装的应用' '学习通作业看板已安装。可修复缺失的程序文件，或卸载此应用。卸载会保留账号、登录数据及个人设置。'
Control MaintenanceDlg Next PushButton 150 243 70 19 '修复' '' Remove
Control MaintenanceDlg Remove PushButton 230 243 70 19 '卸载' '' Cancel
Event MaintenanceDlg Next '[REINSTALL]' ALL 1
Event MaintenanceDlg Next '[REINSTALLMODE]' amus 2
Event MaintenanceDlg Next EndDialog Return 3
Event MaintenanceDlg Remove '[REMOVE]' ALL 1
Event MaintenanceDlg Remove EndDialog Return 2

Dialog ProgressDlg '学习通作业看板 · 正在处理' 1 Cancel
Frame ProgressDlg '正在处理，请稍候' '正在安装、修复或卸载学习通作业看板。请等待操作完成。'
Sql 'UPDATE `Control` SET `Control_Next`=''Cancel'' WHERE `Dialog_`=''ProgressDlg'' AND `Control`=''Cancel'''
Control ProgressDlg Action Text 22 126 355 30 '正在准备…'
Control ProgressDlg Progress ProgressBar 22 165 355 14 '' '' '' 65537
Control ProgressDlg Data Text 22 190 355 25 ''
Sql 'CREATE TABLE `EventMapping` (`Dialog_` CHAR(72) NOT NULL, `Control_` CHAR(50) NOT NULL, `Event` CHAR(50) NOT NULL, `Attribute` CHAR(50) NOT NULL PRIMARY KEY `Dialog_`, `Control_`, `Event`)'
foreach ($mapping in @(@('Action','ActionText','Text'),@('Data','ActionData','Text'),@('Progress','SetProgress','Progress'))) {
    Row EventMapping @('Dialog_','Control_','Event','Attribute') @('ProgressDlg',$mapping[0],$mapping[1],$mapping[2])
}
Sql 'CREATE TABLE `ActionText` (`Action` CHAR(72) NOT NULL, `Description` CHAR(64) LOCALIZABLE, `Template` CHAR(128) LOCALIZABLE PRIMARY KEY `Action`)'
foreach ($action in @(@('InstallFiles','正在复制程序文件…','[1]'),@('RemoveFiles','正在移除程序文件…','[1]'),@('CreateShortcuts','正在创建快捷方式…','[1]'),@('WriteRegistryValues','正在记录安装设置…',''),@('RemoveRegistryValues','正在清理安装设置…',''),@('RegisterProduct','正在注册应用…',''),@('InstallFinalize','正在完成操作…',''))) {
    Row ActionText @('Action','Description','Template') $action
}

foreach ($id in @('ExitDlg','ErrorDlg','CancelDlg')) {
    Dialog $id '学习通作业看板 · 操作结果'
    $heading = switch ($id) { ExitDlg {'操作成功完成'} ErrorDlg {'操作未能完成'} CancelDlg {'安装已取消'} }
    $body = switch ($id) {
        ExitDlg {'向导已完成所选操作。安装成功后，可以从桌面或开始菜单打开学习通作业看板。点击「完成」退出向导。'}
        ErrorDlg {'操作未能完成，已执行的更改会尽可能回滚。请检查安装权限和磁盘空间后重试。如需排查问题，可使用 msiexec /l*v 保存日志。'}
        CancelDlg {'操作已取消。您可以稍后重新运行安装包。点击「完成」退出向导。'}
    }
    Frame $id $heading $body
    Control $id Next PushButton 230 243 70 19 '完成' '' Cancel
    Event $id Next EndDialog Return
}

foreach ($action in @(@('FindRelatedProducts',25),@('LaunchConditions',100),@('CostInitialize',800),@('FileCost',900),@('CostFinalize',1000),@('ExecuteAction',1300))) {
    Row InstallUISequence @('Action','Sequence') $action
}
foreach ($sequence in @('InstallUISequence','InstallExecuteSequence')) {
    Row $sequence @('Action','Condition','Sequence') @('AppSearch','NOT INSTALLDIR',50)
}
Row InstallUISequence @('Action','Condition','Sequence') @('WelcomeDlg','NOT Installed AND NOT REMOVE',1230)
Row InstallUISequence @('Action','Condition','Sequence') @('MaintenanceDlg','Installed AND NOT REMOVE AND NOT REINSTALL',1240)
Row InstallUISequence @('Action','Sequence') @('ProgressDlg',1290)
Row InstallUISequence @('Action','Sequence') @('ExitDlg',-1)
Row InstallUISequence @('Action','Sequence') @('CancelDlg',-2)
Row InstallUISequence @('Action','Sequence') @('ErrorDlg',-3)
