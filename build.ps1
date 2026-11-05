param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $PSScriptRoot ('work/build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage,$target | Out-Null
dotnet publish (Join-Path $PSScriptRoot 'StudyTasks/StudyTasks.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:NuGetAudit=false -o (Join-Path $stage 'publish')
if ($LASTEXITCODE) { throw 'Application build failed' }
$exe = Join-Path $stage 'chaoxingTaskPanel.exe'
& python.exe (Join-Path $PSScriptRoot 'Common/add-startup-check.py') (Join-Path $stage 'publish/chaoxingTaskPanel.exe') $exe
if ($LASTEXITCODE) { throw 'Native compatibility startup check failed' }
& python.exe (Join-Path $PSScriptRoot 'verification/check-published-image.py') $exe
if ($LASTEXITCODE) { throw 'Apphost compatibility and protection flags failed verification' }
if ((Get-Item -LiteralPath $exe).Length -ge 100000000) { throw 'Executable exceeds the 100 MB size limit' }
& (Join-Path $PSScriptRoot 'build-installer.ps1') -Executable $exe -Output (Join-Path $stage 'chaoxingTaskPanel-installer.msi')
Copy-Item -LiteralPath $exe -Destination (Join-Path $target 'chaoxingTaskPanel.exe') -Force
Copy-Item -LiteralPath (Join-Path $stage 'chaoxingTaskPanel-installer.msi') -Destination $target -Force
Write-Output "Build complete: $target"
