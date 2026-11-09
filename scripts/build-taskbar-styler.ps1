param(
    [Parameter(Mandatory=$true)][string]$WindhawkPath,
    [string]$OutputDirectory = 'artifacts/taskbar-styler-build'
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
    $arguments = @('-std=c++23', '-O2', '-s', '-DUNICODE', '-D_UNICODE',
        '-DWINVER=0x0A00', '-D_WIN32_WINNT=0x0A00', '-D_WIN32_IE=0x0A00',
        '-DNTDDI_VERSION=0x0A000008', '-D__USE_MINGW_ANSI_STDIO=0', '-DWH_MOD',
        '-DWH_MOD_ID=L"windows-11-taskbar-styler"', '-DWH_MOD_VERSION=L"1.11"',
        '-include', 'windhawk_api.h', '-target', 'x86_64-w64-mingw32',
        "-ffile-prefix-map=$repo=.", "-ffile-prefix-map=$toolchain=toolchain")
    & $compiler @arguments -shared $engine -x c++ `
        third_party/ExplorerThemeRuntime/licenses/windows-11-taskbar-styler.wh.cpp `
        '-Wl,--export-all-symbols' -o (Join-Path $out 'windows-11-taskbar-styler_1.11.dll') `
        -lcomctl32 -lgdi32 -lole32 -loleaut32 -lruntimeobject -lshlwapi *> (Join-Path $out 'build.log')
    if ($LASTEXITCODE -ne 0) { throw "Taskbar mod build failed. See $out/build.log" }
    Get-FileHash (Join-Path $out 'windows-11-taskbar-styler_1.11.dll') -Algorithm SHA256
} finally { Pop-Location }
