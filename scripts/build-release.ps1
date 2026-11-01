param(
    [string]$Version,
    [string]$Configuration = 'Release',
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$versionSource = Get-Content -LiteralPath (Join-Path $repo 'src\QQMusicWallpaper\AppServices.cs') -Raw
$versionMatch = [regex]::Match($versionSource, 'public const string Version\s*=\s*"([^"]+)"')
if (-not $versionMatch.Success) { throw 'AppInfo.Version was not found.' }
$sourceVersion = $versionMatch.Groups[1].Value
if ([string]::IsNullOrWhiteSpace($Version)) { $Version = $sourceVersion }
if ($Version -notmatch '^\d+\.\d+\.\d+$' -or $Version -ne $sourceVersion) {
    throw 'Package version must match the three-part AppInfo.Version.'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repo 'artifacts' }
if (-not [System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot = Join-Path $repo $OutputRoot }
$artifacts = [System.IO.Path]::GetFullPath($OutputRoot)
if ($artifacts.Length -gt [System.IO.Path]::GetPathRoot($artifacts).Length) {
    $artifacts = $artifacts.TrimEnd('\')
}
$controllerOut = Join-Path $artifacts 'controller'
$rendererOut = Join-Path $artifacts 'renderer'
$objOut = Join-Path $artifacts 'obj-renderer'
$buildLogs = Join-Path $artifacts 'build-logs'
$packageDir = Join-Path $repo 'src\packages'
$portableName = "Piaoklxt-MusicWallpaper-v$Version-win-x64"
$portableDir = Join-Path $artifacts $portableName
$zipPath = Join-Path $artifacts "$portableName.zip"

New-Item -ItemType Directory -Force $artifacts, $controllerOut, $rendererOut, $buildLogs | Out-Null
dotnet build (Join-Path $repo 'src\QQMusicWallpaper\Piaoklxt-MusicWallpaper.csproj') -c $Configuration -o $controllerOut "-bl:$buildLogs\controller-{}.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Controller build failed.' }
$controllerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $controllerOut 'Piaoklxt-MusicWallpaper.exe')).FileVersion
if ($controllerVersion -ne "$Version.0") { throw 'Built controller version does not match the package version.' }

$packageMarker = Join-Path $packageDir 'Microsoft.Windows.CppWinRT.2.0.240405.15\build\native\Microsoft.Windows.CppWinRT.props'
if (-not (Test-Path $packageMarker)) {
    $nuget = Get-Command nuget.exe -ErrorAction SilentlyContinue
    if (-not $nuget) { throw 'nuget.exe was not found in PATH.' }
    & $nuget.Source restore (Join-Path $repo 'src\Renderer\packages.config') -PackagesDirectory $packageDir -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'Visual Studio 2022 Build Tools were not found.' }
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild was not found.' }

& $msbuild (Join-Path $repo 'src\Renderer\Win32CaptureSample.vcxproj') /m /p:Configuration=$Configuration /p:Platform=x64 /p:TargetName=Piaoklxt-MusicWallpaperRenderer /p:OutDir="$rendererOut\" /p:IntDir="$objOut\" "-bl:$buildLogs\renderer-{}.binlog"
if ($LASTEXITCODE -ne 0) { throw 'Renderer build failed.' }

$portableDir = [System.IO.Path]::GetFullPath($portableDir).TrimEnd('\')
if (-not [System.IO.Path]::GetDirectoryName($portableDir).Equals(
        $artifacts, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Portable directory escaped the output root.'
}
if (Test-Path -LiteralPath $portableDir) {
    if ((Get-Item -LiteralPath $portableDir).Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing to replace a portable directory that is a reparse point.'
    }
    Remove-Item -LiteralPath $portableDir -Recurse -Force
}
New-Item -ItemType Directory -Force $portableDir | Out-Null
Copy-Item (Join-Path $controllerOut 'Piaoklxt-MusicWallpaper.exe') $portableDir
Copy-Item (Join-Path $controllerOut 'Piaoklxt-MusicWallpaper.exe.config') $portableDir
Copy-Item (Join-Path $rendererOut 'Piaoklxt-MusicWallpaperRenderer.exe') $portableDir
Copy-Item (Join-Path $repo 'assets\Piaoklxt-MusicWallpaper.ico') $portableDir
Copy-Item (Join-Path $repo 'README.md') $portableDir
Copy-Item (Join-Path $repo 'README.en.md') $portableDir
Copy-Item (Join-Path $repo 'CHANGELOG.md') $portableDir
Copy-Item (Join-Path $repo 'LICENSE') $portableDir
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $portableDir
Copy-Item (Join-Path $repo 'third_party\ExplorerThemeRuntime') (Join-Path $portableDir 'ExplorerThemeRuntime') -Recurse

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "portable_dir=$portableDir"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "zip_path=$zipPath"
}
Write-Host "Created $zipPath"
