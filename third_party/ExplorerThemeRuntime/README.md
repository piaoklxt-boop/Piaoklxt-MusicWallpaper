# Explorer theme runtime

This directory contains the minimal runtime used by Piaoklxt-MusicWallpaper to apply five File Explorer XAML accent-color rules at 28% opacity over opaque header backgrounds. The file-list content keeps an acrylic backdrop. The bundled mod handles the classic controls' alpha rendering; XAML rules alone only style the header. Opaque backgrounds on the header root and header controls prevent its accent layers from revealing the acrylic behind them.

- Windhawk 1.7.3 runtime: GNU GPL v3, https://github.com/ramensoftware/windhawk/releases/tag/v1.7.3
- Windows 11 File Explorer Styler 1.7 by m417z: GNU GPL v3, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-file-explorer-styler.wh.cpp
- The corresponding Windhawk 1.7.3 source is in `licenses/windhawk-1.7.3-source.zip`.
- The exact mod source distributed with the binary is in `licenses/windows-11-file-explorer-styler.wh.cpp`.
- The GPL v3 text is in `licenses/GPL-3.0.txt`.

The runtime is an independent process and separate work aggregated with the MIT-licensed Piaoklxt-MusicWallpaper application. No Windhawk UI, compiler, updater, or service is included. Piaoklxt-MusicWallpaper starts it in portable mode with its tray icon hidden.
