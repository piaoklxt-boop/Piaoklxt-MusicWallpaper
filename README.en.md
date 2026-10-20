# Piaoklxt-MusicWallpaper

[简体中文](README.md)

[![Build portable release](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/actions/workflows/build.yml/badge.svg)](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/actions/workflows/build.yml)

Turn the current QQ Music window into an interactive Windows desktop wallpaper. The wallpaper keeps QQ Music's animated playback page, lets you click the familiar playback controls on the desktop, and can synchronize the Windows accent color with the current page or album artwork.

## Highlights

- Live QQ Music rendering behind normal desktop icons, without keeping the QQ Music window in the foreground.
- Working desktop playback, volume, playback-order and lyric-wheel controls.
- Automatic Windows accent, taskbar, supported app chrome and File Explorer color synchronization.
- A portable x64 package: extract, run, and let the app find or open QQ Music.
- Automatic color restoration on wallpaper shutdown or application exit.

## Download and run

1. Download `Piaoklxt-MusicWallpaper-v1.1.0-win-x64.zip` from [Releases](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/releases).
2. Extract the complete ZIP to any writable folder.
3. Run `Piaoklxt-MusicWallpaper.exe`.

No compiler, SDK, runtime setup, or manual configuration is required. QQ Music itself is not bundled; install the official Windows client first. The app detects and opens QQ Music automatically when it starts. Use the tray menu to re-detect or select `QQMusic.exe` manually if automatic detection fails.

## Controls

- `Ctrl+Alt+U`: show or hide the desktop wallpaper.
- Tray icon: toggle wallpaper, restore QQ Music, choose the player path, toggle startup, control theme synchronization, check for updates, view author information, or exit.
- The desktop volume and playback-order controls use stable wallpaper pop-ups while applying changes to QQ Music in the background.
- Startup with Windows is enabled on first run and can be disabled from the tray menu.

Settings and logs are stored in `%LOCALAPPDATA%\Piaoklxt-MusicWallpaper`, so the extracted program folder stays portable and read-only friendly. Existing settings from the former `QQMusicWallpaper` folder are imported automatically on first run.

## Compatibility

- Windows 11 x64: supported.
- Windows 10 version 1903 or later: experimental.
- QQ Music for Windows: required and installed separately.

The main QQ Music window is mirrored. Clicking a lyric timestamp on the rendered desktop does not currently seek playback, although lyric scrolling works. The comment button is disabled on the wallpaper because QQ Music renders its comment panel as a separate native surface that cannot be mirrored reliably.

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
