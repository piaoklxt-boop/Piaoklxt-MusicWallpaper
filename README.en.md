# Piaoklxt-MusicWallpaper

The local v1.2.5 candidate recognizes owned untitled accompaniment panels and positions native panels near their launcher, including bounded correction after shared-panel reuse or delayed native layout. It also fixes hidden translation controls stealing accompaniment clicks and independent popup projections turning blank desktop clicks into false failures.

The local **v1.2.5 candidate** centralizes native accessory-window lifecycle management and adds tray actions to export interaction diagnostics and recover desktop interaction. A memory-only buffer retains the last 32 routed desktop interactions; exit clears it. Manual exports contain allowlisted technical fields only, excluding song/lyric/account text, window titles, paths and screenshots. No diagnostics are uploaded. Three consecutive failures trigger a tray notice with a one-minute cooldown; isolated cancellation stays quiet. Recovery does not restart QQ Music or change playback. Ambiguous native windows are not presented. This candidate still needs live acceptance; release download links below refer to v1.2.1.

[简体中文](README.md)

[![Build portable release](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/actions/workflows/build.yml/badge.svg)](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/actions/workflows/build.yml)

Turn the current QQ Music window into an interactive Windows desktop wallpaper. The wallpaper keeps QQ Music's animated playback page, lets you click the familiar playback controls on the desktop, and can synchronize the Windows accent color with the current page or album artwork.

## Highlights

- Live QQ Music rendering behind normal desktop icons, without keeping the QQ Music window in the foreground.
- Working desktop playback, click-and-drag progress seeking, lyric-timestamp seeking, volume, playback-order and lyric-wheel controls.
- The comment button opens QQ Music's separate native comment window; a blank desktop click dismisses it while the main player remains behind the desktop.
- Automatic Windows accent, taskbar, supported app chrome and File Explorer color synchronization.
- A portable x64 package: extract, run, and let the app find or open QQ Music.
- Automatic color restoration on wallpaper shutdown or application exit.

## What's new in v1.2.1

### Fixed and improved

- Show an immediate drag preview with a slim rail, small thumb and background matching the current player skin; still commit one seek on release.
- Fix comment dispatch selecting an old player bar and recover an initially missed comment click through a bounded semantic probe that cancels stale requests.
- Preserve the control-cache deadline during unchanged window maintenance while refreshing after real movement or recovery.
- Reduce mouse-hook recovery caused by delayed polling and add optional local diagnostics.
- Run five regression suites and playback-mode checks before packaging, including actual window messages and preview rendering.

See [CHANGELOG.md](CHANGELOG.md) for the complete history.

## Download and run

1. Download `Piaoklxt-MusicWallpaper-v1.2.1-win-x64.zip` from [Releases](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/releases).
2. Extract the complete ZIP to any writable folder.
3. Run `Piaoklxt-MusicWallpaper.exe`.

No compiler, SDK, runtime setup, or manual configuration is required. QQ Music itself is not bundled; install the official Windows client first. The app detects and opens QQ Music automatically when it starts. Use the tray menu to re-detect or select `QQMusic.exe` manually if automatic detection fails.

## Controls

- `Ctrl+Alt+U`: show or hide the desktop wallpaper.
- Tray icon: toggle wallpaper, restore QQ Music, choose the player path, toggle startup, control theme synchronization, check for updates, view author information, or exit.
- The desktop volume and playback-order controls use stable wallpaper pop-ups while applying changes to QQ Music in the background.
- Click or drag the desktop progress bar to seek, with a live preview while dragging and one seek on release; exposed lyric timestamps also seek directly. Play, previous, and next prefer QQ Music's exposed control bounds instead of broad coordinate targets.
- Clicking comments opens the separate native comment window through the exposed bounds in the background; the QQ Music main player is never moved in front of the desktop.
- Startup with Windows is enabled on first run and can be disabled from the tray menu.

Settings and logs are stored in `%LOCALAPPDATA%\Piaoklxt-MusicWallpaper`, so the extracted program folder stays portable and read-only friendly. Existing settings from the former `QQMusicWallpaper` folder are imported automatically on first run.

## Compatibility

- Windows 11 x64: supported.
- Windows 10 version 1903 or later: experimental.
- QQ Music for Windows: required and installed separately.

The main QQ Music window is mirrored. Dragging the bottom progress bar keeps the real Windows cursor moving and submits one mapped seek to QQ Music on release; exposed lyric timestamps seek playback directly, and lyric scrolling continues to work. Because QQ Music renders comments as a separate native top-level window, the comment action opens and restores only that window, and a blank desktop click dismisses it while the main player stays behind the desktop.

On Windows 11, the portable package includes an optional minimal Explorer styling runtime. It colors the File Explorer top area with the synchronized accent color and does not require a separate Windhawk installation. If an existing Windhawk instance is already running, the bundled runtime stays inactive to avoid conflicts.

When desktop rendering starts, the app saves the current Windows color settings. Turning the wallpaper off or exiting the app restores those settings; turning it on again starts a fresh color session.

## Privacy and updates

The app contains no telemetry. Update checks make a standard HTTPS request to this repository's GitHub Releases API. You can also check manually from the tray menu.

## Build from source

Requirements: Visual Studio 2022 Build Tools with Desktop development with C++, a .NET SDK capable of building `net40`, and NuGet CLI. Run `scripts\build-release.ps1`; the portable folder and ZIP are written to `artifacts`.

## License and trademarks

The Piaoklxt-MusicWallpaper application is released under the [MIT License](LICENSE). The portable archive also contains a separate GPLv3 Explorer styling runtime; its source and license are shipped beside its binaries. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

This is an independent community project. It is not affiliated with or endorsed by Tencent. QQ Music and related names and marks belong to their respective owners. The project does not distribute QQ Music or Tencent assets.

## Author

piaoklxt — <piaoklxt@gmail.com>
