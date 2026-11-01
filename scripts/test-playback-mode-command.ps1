$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$controller = (Get-Content -LiteralPath (Join-Path $repo 'src\QQMusicWallpaper\WallpaperContext.cs') -Raw -Encoding UTF8) -replace "`r`n?", "`n"
$overlay = (Get-Content -LiteralPath (Join-Path $repo 'src\QQMusicWallpaper\PlaybackModeOverlayForm.cs') -Raw -Encoding UTF8) -replace "`r`n?", "`n"

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
if ($controller.Contains('new ProcessStartInfo(qqPath,' + "`n`t`t`t`t`t`"/playmode`"") -or
    $controller -match 'ProcessStartInfo\(qqPath,\s*"/playmode"\s*\)') {
    throw 'The parameterless /playmode regression is present.'
}

if (-not $controller.Contains('case DesktopSpecialAction.ShowPlaybackModes:' + "`n`t`t`t`tQueueShowPlaybackModes();")) {
    throw 'Playback mode button is not routed to the control-based mode picker.'
}
if (-not $overlay.Contains('new Button')) {
    throw 'Playback mode picker does not expose native button controls.'
}
if ($overlay.Contains('MouseDown +=') -or $overlay.Contains('(e.Y - 8) / 42')) {
    throw 'Playback mode picker still infers selections from pointer coordinates.'
}

Write-Host 'PASS: playback modes use native buttons and explicit QQ Music mode values.'
