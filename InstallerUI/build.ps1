param([Parameter(Mandatory)][string]$OutputDirectory,[switch]$Preview,[switch]$HeadlessTest)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$target=[IO.Path]::GetFullPath($OutputDirectory)
Push-Location $PSScriptRoot
try {
    & python.exe '../Common/prepare-policy.py'
    if($LASTEXITCODE){throw 'Compatibility policy generation failed'}
    & windres.exe 'ModernUI.rc' -O coff -o (Join-Path $target 'ModernUI-res.o')
    if($LASTEXITCODE){throw 'UI resources failed to build'}
    $common=@('-std=c++17','-O2','-static','-static-libgcc','-static-libstdc++','-finput-charset=UTF-8',
        'ModernUI.cpp','UserDataCleanup.cpp',(Join-Path $target 'ModernUI-res.o'),'-lmsi','-lole32','-luuid','-lshell32','-lcomctl32','-luxtheme','-lgdi32','-ladvapi32','-lruntimeobject')
    & g++.exe '-shared' '-o' (Join-Path $target 'ModernUI.dll') @common
    if($LASTEXITCODE){throw 'Modern MSI UI failed to build'}
    if($Preview) {
        & g++.exe '-mwindows' '-municode' '-DPREVIEW_EXE' '-o' (Join-Path $target 'ModernUI-preview.exe') @common
        if($LASTEXITCODE){throw 'Modern MSI preview failed to build'}
    }
    if($HeadlessTest) {
        & g++.exe '-municode' '-DENGINE_TEST_EXE' '-o' (Join-Path $target 'ModernUI-engine-check.exe') @common
        if($LASTEXITCODE){throw 'Modern MSI engine check failed to build'}
    }
}finally {Pop-Location}
