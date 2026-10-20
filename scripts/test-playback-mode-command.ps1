$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$controller = Get-Content -LiteralPath (Join-Path $repo 'src\QQMusicWallpaper\WallpaperContext.cs') -Raw

$requiredControllerFragments = @(
    'case "单曲循环": modeValue = 1;',
    'case "顺序播放": modeValue = 2;',
    'case "列表循环": modeValue = 3;',
    'case "随机播放": modeValue = 4;',
    '"/playmode " + modeValue.ToString('
)
foreach ($fragment in $requiredControllerFragments) {
    if (-not $controller.Contains($fragment)) {
        throw "Missing controller contract: $fragment"
    }
}
if ($controller.Contains('new ProcessStartInfo(qqPath,' + [Environment]::NewLine + "`t`t`t`t`t`"/playmode`"") -or
    $controller -match 'ProcessStartInfo\(qqPath,\s*"/playmode"\s*\)') {
    throw 'The parameterless /playmode regression is present.'
}

Write-Host 'PASS: playback-mode commands map to explicit QQ Music mode values.'
