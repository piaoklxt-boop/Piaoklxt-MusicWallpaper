# Explorer theme runtime

This directory contains the minimal runtime used by Piaoklxt-MusicWallpaper to apply the five File Explorer XAML accent-color rules that were previously configured through a separate Windhawk installation.

- Windhawk 1.7.3 runtime: GNU GPL v3, https://github.com/ramensoftware/windhawk/releases/tag/v1.7.3
- Windows 11 File Explorer Styler 1.7 by m417z: GNU GPL v3, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-file-explorer-styler.wh.cpp
- The corresponding Windhawk 1.7.3 source is in `licenses/windhawk-1.7.3-source.zip`.
- The exact mod source distributed with the binary is in `licenses/windows-11-file-explorer-styler.wh.cpp`.
- The GPL v3 text is in `licenses/GPL-3.0.txt`.

The runtime is an independent process and separate work aggregated with the MIT-licensed Piaoklxt-MusicWallpaper application. No Windhawk UI, compiler, updater, or service is included. Piaoklxt-MusicWallpaper starts it in portable mode with its tray icon hidden.
