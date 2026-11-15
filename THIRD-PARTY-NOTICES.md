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

These GPL components run as an independent process and remain separate works aggregated with the MIT-licensed Piaoklxt-MusicWallpaper application. The corresponding Windhawk 1.7.3 source archive, exact distributed mod source, and GPLv3 text are included under `ExplorerThemeRuntime\licenses`. The package includes only the runtime files needed for Explorer and optional taskbar styling; it does not include the Windhawk user interface, compiler, updater, or service.

QQ Music is not included in this repository or its release archives.

Better file sizes in Explorer details 1.6 by m417z is included as a separate
GPLv3 Windhawk mod, with the MusicWallpaper asynchronous adapter:

- Upstream: https://github.com/ramensoftware/windhawk-mods/blob/main/mods/explorer-details-better-file-sizes.wh.cpp
- Distributed version: `1.6-piaoklxt.1`; restricted to `explorer.exe`.
- Exact corresponding source: `ExplorerThemeRuntime/licenses/explorer-details-better-file-sizes.wh.cpp` and `folder-size-cache.hpp`.
- Rebuild: `scripts/build-folder-sizes.ps1`, using the same external Windhawk 1.7.3 developer toolchain.
- The adapter replaces synchronous recursive size queries with a bounded cache and a single low-priority worker. Existing upstream license and Everything SDK notices are preserved in the source; Everything is not required or bundled.

Windows 11 Taskbar Styler 1.11 by m417z is also distributed under GPLv3 as a separate mod:

- Pinned source: https://github.com/ramensoftware/windhawk-mods/blob/2cd9c600bfe13afb4fba68ec907a4b8084acc0a3/mods/windows-11-taskbar-styler.wh.cpp
- Exact unmodified source: `ExplorerThemeRuntime/licenses/windows-11-taskbar-styler.wh.cpp`
- Rebuild script: `scripts/build-taskbar-styler.ps1` using a separate Windhawk 1.7.3 developer toolchain.
