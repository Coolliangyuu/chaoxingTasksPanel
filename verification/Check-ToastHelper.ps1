$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot '../StudyTasks/Toast.ps1'
. ([ScriptBlock]::Create([IO.File]::ReadAllText($scriptPath, [Text.Encoding]::UTF8))) -DefinitionsOnly
function Assert([bool]$ok,[string]$name) { if(-not $ok){throw $name}; Write-Output "PASS: $name" }
function New-TestNotifier([bool]$registered,[string]$setting='Enabled',[int]$errorCode=-2147023728) {
    $notifier=[PSCustomObject]@{ Registered=$registered; Setting=$setting; ErrorCode=$errorCode; Calls=(New-Object 'Collections.Generic.List[string]') }
    $notifier|Add-Member ScriptMethod get_Setting {
        $this.Calls.Add('setting')
        if(-not $this.Registered){throw (New-Object Runtime.InteropServices.COMException('Missing initial notification registration',$this.ErrorCode))}
        return $this.Setting
    }
    $notifier|Add-Member ScriptMethod Show {param($toast) $this.Calls.Add('show'); $this.Registered=$true}
    return $notifier
}
$fresh=New-TestNotifier $false
$result=Send-DesktopToast $fresh 'fake-toast'
Assert ($result.Success -and ($fresh.Calls -join ',') -eq 'setting,show,setting') 'First notification tolerates only E_NOTFOUND, sends, then checks registered status'
$existing=New-TestNotifier $true
$result=Send-DesktopToast $existing 'fake-toast'
Assert ($result.Success -and ($existing.Calls -join ',') -eq 'setting,show,setting') 'Existing enabled notification identity sends normally'
$disabled=New-TestNotifier $true 'DisabledForUser'
$result=Send-DesktopToast $disabled 'fake-toast'
Assert (-not $result.Success -and ($disabled.Calls -join ',') -eq 'setting') 'Windows-disabled notifications report failure without claiming delivery'
$denied=New-TestNotifier $false 'Enabled' -2147024891
$threw=$false
try { [void](Send-DesktopToast $denied 'fake-toast') } catch {$threw=$true}
Assert ($threw -and ($denied.Calls -join ',') -eq 'setting') 'Other status failures are not treated as first registration'
$failed=New-TestNotifier $true
$failed|Add-Member ScriptMethod Show {param($toast) throw (New-Object Runtime.InteropServices.COMException('Show failed',-2147467259))} -Force
$threw=$false
try { [void](Send-DesktopToast $failed 'fake-toast') } catch {$threw=$true}
Assert $threw 'Send failures are propagated for diagnostics and retry'
