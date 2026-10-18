param(
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repo 'artifacts'
$controllerOut = Join-Path $artifacts 'controller'
$rendererOut = Join-Path $artifacts 'renderer'
$objOut = Join-Path $artifacts 'obj-renderer'
$packageDir = Join-Path $repo 'src\packages'
$portableName = "QQMusicWallpaper-v$Version-win-x64"
$portableDir = Join-Path $artifacts $portableName
$zipPath = Join-Path $artifacts "$portableName.zip"

New-Item -ItemType Directory -Force $artifacts, $controllerOut, $rendererOut | Out-Null
dotnet build (Join-Path $repo 'src\QQMusicWallpaper\QQMusicWallpaper.csproj') -c $Configuration -o $controllerOut
if ($LASTEXITCODE -ne 0) { throw 'Controller build failed.' }

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

& $msbuild (Join-Path $repo 'src\Renderer\Win32CaptureSample.vcxproj') /m /p:Configuration=$Configuration /p:Platform=x64 /p:TargetName=QQMusicWallpaperRenderer /p:OutDir="$rendererOut\" /p:IntDir="$objOut\"
if ($LASTEXITCODE -ne 0) { throw 'Renderer build failed.' }

if (Test-Path $portableDir) { [System.IO.Directory]::Delete($portableDir, $true) }
New-Item -ItemType Directory -Force $portableDir | Out-Null
Copy-Item (Join-Path $controllerOut 'QQMusicWallpaper.exe') $portableDir
Copy-Item (Join-Path $controllerOut 'QQMusicWallpaper.exe.config') $portableDir
Copy-Item (Join-Path $rendererOut 'QQMusicWallpaperRenderer.exe') $portableDir
Copy-Item (Join-Path $repo 'assets\QQMusicWallpaper.ico') $portableDir
Copy-Item (Join-Path $repo 'README.md') $portableDir
Copy-Item (Join-Path $repo 'README.zh-CN.md') $portableDir
Copy-Item (Join-Path $repo 'CHANGELOG.md') $portableDir
Copy-Item (Join-Path $repo 'LICENSE') $portableDir
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $portableDir

if (Test-Path $zipPath) { [System.IO.File]::Delete($zipPath) }
Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Created $zipPath"

