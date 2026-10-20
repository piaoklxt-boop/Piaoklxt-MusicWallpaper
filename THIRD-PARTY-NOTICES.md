# Third-party notices

The desktop renderer is derived from Robert Mikhayelyan's `Win32CaptureSample`:

- Source: https://github.com/robmikh/Win32CaptureSample
- Copyright (c) 2019 Robert Mikhayelyan
- License: MIT

The upstream license is preserved at `src/Renderer/LICENSE.Win32CaptureSample` and applies to the derived renderer portions. NuGet dependencies retain their own licenses and notices.

The portable Windows 11 release contains a separate, minimal Explorer theme runtime built from Windhawk 1.7.3:

- Source: https://github.com/ramensoftware/windhawk
- Release: https://github.com/ramensoftware/windhawk/releases/tag/v1.7.3
- Copyright: Michael Maltsev and Windhawk contributors
- License: GNU General Public License version 3

It also contains the Windows 11 File Explorer Styler 1.7 mod by m417z:

- Source: https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-file-explorer-styler.wh.cpp
- License: GNU General Public License version 3

These GPL components run as an independent process and remain separate works aggregated with the MIT-licensed Piaoklxt-MusicWallpaper application. The corresponding Windhawk 1.7.3 source archive, exact distributed mod source, and GPLv3 text are included under `ExplorerThemeRuntime\licenses`. The package includes only the runtime files needed for the five Explorer accent-color rules; it does not include the Windhawk user interface, compiler, updater, or service.

QQ Music is not included in this repository or its release archives.
