param([Parameter(Mandatory=$true)][string]$WindhawkPath,
    [string]$OutputDirectory='artifacts/folder-sizes-build')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$toolchain=(Resolve-Path -LiteralPath $WindhawkPath).Path
$compiler=Join-Path $toolchain 'Compiler/bin/clang++.exe'
$engine=Join-Path $toolchain 'Engine/1.7.3/64/windhawk.lib'
Push-Location $repo
try {
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $out=(Resolve-Path -LiteralPath $OutputDirectory).Path
    $common=@('-std=c++23','-O2','-s','-DUNICODE','-D_UNICODE',
        '-DWINVER=0x0A00','-D_WIN32_WINNT=0x0A00','-D_WIN32_IE=0x0A00',
        '-DNTDDI_VERSION=0x0A000008','-D__USE_MINGW_ANSI_STDIO=0','-DWH_MOD',
        '-DWH_MOD_ID=L"explorer-details-better-file-sizes"','-DWH_MOD_VERSION=L"1.6-piaoklxt.1"',
        '-include','windhawk_api.h','-target','x86_64-w64-mingw32',
        "-ffile-prefix-map=$repo=.","-ffile-prefix-map=$toolchain=toolchain")
    & $compiler @common -shared $engine -x c++ third_party/ExplorerThemeRuntime/licenses/explorer-details-better-file-sizes.wh.cpp `
        '-Wl,--export-all-symbols' -o (Join-Path $out 'explorer-details-better-file-sizes_1.6_piaoklxt1.dll') `
        -lole32 -loleaut32 -lpropsys -lshlwapi -luuid *> (Join-Path $out 'build.log')
    if($LASTEXITCODE -ne 0){throw "Folder-size build failed: $out/build.log"}
    & $compiler @common -DWH_EDITING tests/FolderSizes.Tests/CacheTests.cpp -o (Join-Path $out 'FolderSizes.Tests.exe') `
        -lole32 -lshell32 *> (Join-Path $out 'tests-build.log')
    if($LASTEXITCODE -ne 0){throw "Folder-size tests build failed: $out/tests-build.log"}
    Copy-Item third_party/ExplorerThemeRuntime/Seed/Engine/Mods/64/*.whl $out
    & (Join-Path $out 'FolderSizes.Tests.exe') *> (Join-Path $out 'tests-run.log')
    Get-Content (Join-Path $out 'tests-run.log')
    if($LASTEXITCODE -ne 0){throw 'Folder-size tests failed'}
    Get-FileHash (Join-Path $out 'explorer-details-better-file-sizes_1.6_piaoklxt1.dll')
} finally {Pop-Location}
