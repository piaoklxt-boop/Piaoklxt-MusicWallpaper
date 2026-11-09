param(
    [Parameter(Mandatory=$true)][string]$WindhawkPath,
    [string]$OutputDirectory = 'artifacts/explorer-styler-build'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$toolchain = (Resolve-Path -LiteralPath $WindhawkPath).Path
$compiler = Join-Path $toolchain 'Compiler/bin/clang++.exe'
$engine = Join-Path $toolchain 'Engine/1.7.3/64/windhawk.lib'
if (!(Test-Path -LiteralPath $compiler) -or !(Test-Path -LiteralPath $engine)) {
    throw 'Provide an extracted Windhawk 1.7.3 developer toolchain.'
}
Push-Location $repo
try {
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
    $out = (Resolve-Path -LiteralPath $OutputDirectory).Path
    $common = @('-std=c++23', '-O2', '-s', '-DUNICODE', '-D_UNICODE',
        '-DWINVER=0x0A00', '-D_WIN32_WINNT=0x0A00', '-D_WIN32_IE=0x0A00',
        '-DNTDDI_VERSION=0x0A000008', '-D__USE_MINGW_ANSI_STDIO=0', '-DWH_MOD',
        '-DWH_MOD_ID=L"windows-11-file-explorer-styler"',
        '-DWH_MOD_VERSION=L"1.7-piaoklxt.9"', '-include', 'windhawk_api.h',
        '-target', 'x86_64-w64-mingw32', "-ffile-prefix-map=$repo=.")
    $libs = @('-lcomctl32', '-ld2d1', '-ldwmapi', '-lgdi32', '-lmsimg32',
        '-lole32', '-loleaut32', '-lruntimeobject', '-lshlwapi', '-luxtheme')
    $source = 'third_party/ExplorerThemeRuntime/licenses/windows-11-file-explorer-styler.wh.cpp'
    & $compiler @common -shared $engine -x c++ $source '-Wl,--export-all-symbols' `
        -o (Join-Path $out 'windows-11-file-explorer-styler_1.7_piaoklxt9.dll') @libs `
        *> (Join-Path $out 'mod-build.log')
    if ($LASTEXITCODE -ne 0) { throw "Native mod build failed. See $out/mod-build.log" }
    & $compiler @common -DWH_EDITING tests/ExplorerStyler.Tests/HeaderTests.cpp `
        -o (Join-Path $out 'ExplorerStyler.Tests.exe') @libs `
        *> (Join-Path $out 'tests-build.log')
    if ($LASTEXITCODE -ne 0) { throw "Header regression build failed. See $out/tests-build.log" }
    Copy-Item third_party/ExplorerThemeRuntime/Seed/Engine/Mods/64/*.whl $out
    & (Join-Path $out 'ExplorerStyler.Tests.exe') *> (Join-Path $out 'tests-run.log')
    $testExit = $LASTEXITCODE
    Get-Content (Join-Path $out 'tests-run.log')
    if ($testExit -ne 0) { throw 'Header brush regressions failed.' }
    Get-FileHash (Join-Path $out 'windows-11-file-explorer-styler_1.7_piaoklxt9.dll') -Algorithm SHA256
} finally { Pop-Location }
