# QQ Music Wallpaper for Windows

[简体中文](README.zh-CN.md)

[![Build portable release](https://github.com/piaoklxt-boop/QQMusicWallpaper/actions/workflows/build.yml/badge.svg)](https://github.com/piaoklxt-boop/QQMusicWallpaper/actions/workflows/build.yml)

Turn the current QQ Music window into an interactive Windows desktop wallpaper. The wallpaper keeps QQ Music's animated playback page, lets you click the familiar playback controls on the desktop, and can synchronize the Windows accent color with the current page or album artwork.

## Download and run

1. Download `QQMusicWallpaper-v1.0.0-win-x64.zip` from [Releases](https://github.com/piaoklxt-boop/QQMusicWallpaper/releases).
2. Extract the complete ZIP to any writable folder.
3. Run `QQMusicWallpaper.exe`.

No compiler, SDK, runtime setup, or manual configuration is required. QQ Music itself is not bundled; install the official Windows client first. The app detects common install locations and running processes. Use the tray menu to re-detect or select `QQMusic.exe` manually.

## Controls

- `Ctrl+Alt+U`: show or hide the desktop wallpaper.
- Tray icon: toggle wallpaper, restore QQ Music, choose the player path, toggle startup, control theme synchronization, check for updates, or exit.
- Startup with Windows is enabled on first run and can be disabled from the tray menu.

Settings and logs are stored in `%LOCALAPPDATA%\QQMusicWallpaper`, so the extracted program folder stays portable and read-only friendly.

## Compatibility

- Windows 11 x64: supported.
- Windows 10 version 1903 or later: experimental.
- QQ Music for Windows: required and installed separately.

The main QQ Music window is mirrored. Some independent pop-ups, such as certain comment windows, may remain in the original QQ Music window instead of appearing inside the wallpaper.

## Privacy and updates

The app contains no telemetry. Update checks make a standard HTTPS request to this repository's GitHub Releases API. You can also check manually from the tray menu.

## Build from source

Requirements: Visual Studio 2022 Build Tools with Desktop development with C++, a .NET SDK capable of building `net40`, and NuGet CLI. Run `scripts\build-release.ps1`; the portable folder and ZIP are written to `artifacts`.

## License and trademarks

The project is released under the [MIT License](LICENSE). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for incorporated third-party work.

This is an independent community project. It is not affiliated with or endorsed by Tencent. QQ Music and related names and marks belong to their respective owners. The project does not distribute QQ Music or Tencent assets.

