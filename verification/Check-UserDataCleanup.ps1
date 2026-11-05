param([Parameter(Mandatory)][string]$WorkDirectory)
$ErrorActionPreference='Stop'
$base=[IO.Path]::GetFullPath($WorkDirectory)
$workspace=[IO.Path]::GetFullPath((Get-Location).Path).TrimEnd('\')+'\'
if(-not $base.StartsWith($workspace,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup fixtures must remain in the workspace'}
$root=Join-Path $base ('cleanup-fixture-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
Set-Content -LiteralPath (Join-Path $root '.codex-cleanup-tests') -Value 'Isolated cleanup fixture; no real app data'
foreach($path in @('normal/ChaoxingStudyTasks/账号 缓存','outside/ChaoxingStudyTasks','linked-leaf','locked/ChaoxingStudyTasks')) {
 New-Item -ItemType Directory -Path (Join-Path $root $path) -Force | Out-Null
}
foreach($path in @('normal/ChaoxingStudyTasks/settings.json','normal/ChaoxingStudyTasks/账号 缓存/cache.txt','outside/keep.txt','outside/ChaoxingStudyTasks/keep.txt','locked/ChaoxingStudyTasks/busy.txt')) {
 Set-Content -LiteralPath (Join-Path $root $path) -Value 'fixture data'
}
[IO.File]::SetAttributes((Join-Path $root 'normal/ChaoxingStudyTasks/账号 缓存/cache.txt'),[IO.FileAttributes]::ReadOnly)
New-Item -ItemType Junction -Path (Join-Path $root 'normal/ChaoxingStudyTasks/junction') -Target (Join-Path $root 'outside') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $root 'linked-leaf/ChaoxingStudyTasks') -Target (Join-Path $root 'outside') | Out-Null
New-Item -ItemType Junction -Path (Join-Path $root 'linked-parent') -Target (Join-Path $root 'outside') | Out-Null
$exe=Join-Path $base 'UserDataCleanupChecks.exe'
& g++.exe '-std=c++17' '-O2' '-static' '-static-libgcc' '-static-libstdc++' '-municode' '-finput-charset=UTF-8' `
 (Join-Path $PSScriptRoot 'UserDataCleanupChecks.cpp') (Join-Path $PSScriptRoot '../InstallerUI/UserDataCleanup.cpp') `
 '-lmsi' '-lole32' '-luuid' '-lshell32' '-ladvapi32' '-lruntimeobject' '-o' $exe
if($LASTEXITCODE){throw 'Cleanup checks failed to compile'}
& $exe $root
if($LASTEXITCODE){throw 'Cleanup checks failed'}
Write-Output ('Fixture root: '+$root)
