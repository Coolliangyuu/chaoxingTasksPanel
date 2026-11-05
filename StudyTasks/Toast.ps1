param([switch]$DefinitionsOnly)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Get-DesktopToastSetting($Notifier, [bool]$AllowUnregistered) {
    try { return $Notifier.get_Setting().ToString() }
    catch {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        # A valid installed AUMID has no notification settings until its first Show.
        if ($AllowUnregistered -and $cause.HResult -eq -2147023728) { return $null }
        throw
    }
}

function Send-DesktopToast($Notifier, $Toast) {
    $setting = Get-DesktopToastSetting $Notifier $true
    if ($setting -and $setting -ne 'Enabled') {
        return [PSCustomObject]@{ Success=$false; Message="Windows 已禁用此通知（$setting）。请在系统通知设置中检查本应用。"; Channel='Windows' }
    }
    $Notifier.Show($Toast)
    $setting = Get-DesktopToastSetting $Notifier $false
    if ($setting -ne 'Enabled') {
        return [PSCustomObject]@{ Success=$false; Message="Windows 未允许此通知（$setting）。请检查系统通知设置。"; Channel='Windows' }
    }
    return [PSCustomObject]@{ Success=$true; Message='Windows 已接收通知。'; Channel='Windows' }
}

if ($DefinitionsOnly) { return }

$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
try {
    $payload = [Console]::In.ReadToEnd() | ConvertFrom-Json
    [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
    [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
    $title = [Security.SecurityElement]::Escape($payload.Title)
    $message = [Security.SecurityElement]::Escape($payload.Message)
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>$title</text><text>$message</text></binding></visual></toast>")
    $notifier = [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($payload.AppId)
    $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
    $toast.ExpirationTime = [DateTimeOffset]::Now.AddMinutes(5)
    $toast.Tag = [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $toast.Group = 'StudyTasks'
    $result = Send-DesktopToast $notifier $toast
    if ($result.Success -and $payload.Test) {
        $found = $false
        for ($attempt=0; $attempt -lt 10; $attempt++) {
            $history = [Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory($payload.AppId)
            foreach ($item in $history) { if ($item.Tag -eq $toast.Tag) { $found = $true; break } }
            if ($found) { break }
            Start-Sleep -Milliseconds 100
        }
        if ($found) { $result.Message = '测试通知已进入 Windows 通知中心。' }
        else { $result.Message = 'Windows 已接收测试通知；若未显示，请检查通知横幅及勿扰模式。' }
    }
    [Console]::Out.WriteLine(($result | ConvertTo-Json -Compress))
    if ($result.Success) { exit 0 } else { exit 2 }
} catch {
    $cause = $_.Exception
    while ($cause.InnerException) { $cause = $cause.InnerException }
    $result = [PSCustomObject]@{ Success=$false; Message=('Windows 通知发送失败：' + $cause.Message); Channel='Windows' }
    [Console]::Out.WriteLine(($result | ConvertTo-Json -Compress))
    exit 1
}
