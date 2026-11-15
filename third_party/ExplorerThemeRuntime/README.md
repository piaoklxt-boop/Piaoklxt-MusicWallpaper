# Explorer theme runtime

The runtime also includes the GPL Better file sizes mod `1.6-piaoklxt.1`, with
an asynchronous, low-priority folder-size cache. Rebuild it using
`scripts/build-folder-sizes.ps1`; its exact source and adapter header are under
`licenses`. Only this new versioned DLL is added when the runtime is already
active. Its enable switch updates the module in place without unloading XAML
styling or restarting Explorer. See `docs/desktop-pinning-and-folder-sizes.md`
in the development repository for calculation bounds.

This directory contains the minimal runtime used by Piaoklxt-MusicWallpaper to style File Explorer. Named address-bar and command-bar XAML controls use the original 38% accent tint flattened over the dark inactive-window base into one opaque color. This avoids both focus-dependent backdrop colors and black backing panels between nested controls. The classic file, navigation and preview panes share an accent-acrylic background with a lighter neutral tint.

- Windhawk 1.7.3 runtime: GNU GPL v3, https://github.com/ramensoftware/windhawk/releases/tag/v1.7.3
- Windows 11 File Explorer Styler 1.7 by m417z, with the Piaoklxt tint and opaque-header modifications (1.7-piaoklxt.11): GNU GPL v3, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-file-explorer-styler.wh.cpp
- The corresponding Windhawk 1.7.3 source is in `licenses/windhawk-1.7.3-source.zip`.
- The exact mod source distributed with the binary is in `licenses/windows-11-file-explorer-styler.wh.cpp`.
- The GPL v3 text is in `licenses/GPL-3.0.txt`.

The runtime is an independent process and separate work aggregated with the MIT-licensed Piaoklxt-MusicWallpaper application. No Windhawk UI, compiler, updater, or service is included. Piaoklxt-MusicWallpaper starts it in portable mode with its tray icon hidden.

To rebuild the modified x64 mod, provide a separate extracted Windhawk 1.7.3 developer toolchain to `scripts/build-explorer-styler.ps1 -WindhawkPath <toolchain>`. The script retains compiler logs, emits the mod DLL, and tests the production header parser and opaque-color composition; it does not install or start Windhawk. The compiler and its UI must not be copied into the portable application.

The configuration uses `acrylicblur` (the upstream accent-acrylic path), rather than the focus-dependent system Mica backdrop. Its neutral tint is locally adjusted. The optional `OpaqueBase="True"` extension to `WindhawkBlur` produces an opaque composition color brush, with no backdrop sampling or blur, while retaining the upstream theme-resource change notifications. Existing blur rules are unchanged unless this option is enabled. Header rules match named XAML controls; no screen positions or input interception are added. Confirm both focused and unfocused appearance in Explorer: a successful compositor attachment or DWM property check alone does not establish that a background is visible through classic controls.

The controller generates the active configuration from the packaged seed plus separately saved user preferences. `piaoklxtAcrylicOpacity` (0–255), `piaoklxtAcrylicGray` (0–200), and `piaoklxtSidebarAcrylic` (0/1) configure the native paint path. Turning off navigation acrylic writes opaque alpha only for descendants of the native navigation control; file-view backgrounds retain their own rendering. Header rules are regenerated for the selected mode. Applying changes updates only the affected module INI in place, using the runtime's native settings watcher and a distinct revision for every update. It does not restart the styling runtime, Explorer or QQ Music. Renderer-only changes do not touch either native module. A bounded desktop-host observation detects shell loss and rolls back changed module settings before preferences can be saved. This observation does not acknowledge that every XAML rule has rendered.

Caption-hosting tab containers and generic content roots must not receive opaque background overrides: they obscure native window caption buttons and hover feedback. Navigation row backgrounds include zero-character ETO_OPAQUE clears; these preserve opaque alpha when navigation acrylic is disabled.

Opaque navigation uses the exact resolved address/command-bar brush color, including tint strength, and repaints when that color changes. The native tab/title bar and caption buttons retain system styling.

`piaoklxtFileAreaAcrylic` (0/1, default 1) independently controls the native file-list surface and its empty background. The setting resolves descendants of `SHELLDLL_DefView` from the live paint owner, including tracked memory DCs. Disabling it preserves solid fill colors and writes opaque alpha through FillRect, PatBlt and ETO_OPAQUE row clears; navigation and title controls retain their separate settings.

## Optional taskbar styling

The seed also contains the unmodified Windows 11 Taskbar Styler 1.11 at upstream commit `2cd9c600bfe13afb4fba68ec907a4b8084acc0a3`, under GPLv3. Its source is preserved in `licenses/windows-11-taskbar-styler.wh.cpp`. Rebuild with `scripts/build-taskbar-styler.ps1` and the same external developer toolchain. The taskbar mod is disabled by default. Its generated rule targets the named taskbar background rectangle and uses the same system accent as Explorer; it does not replace taskbar input.

Changed active mod configurations are restored on apply failure. Taskbar effects are independent of the Explorer master switch. The renderer handles bottom-edge extension separately, reusing its composition surface beneath the primary working area.

## Callback lifetime

The Explorer fork retains an unadvised XAML watcher until its callback threads have crossed a sent-message barrier, and releases the old diagnostics before reinjection. This backports the callback-lifetime handling from the bundled Taskbar Styler 1.11 source at the pinned commit above. The wait is bounded; a hung callback thread remains a limitation. A background advising thread receives its reference before it can run. Native regressions exercise the barrier against an owned message-only window, as well as the existing paint/parser checks.

## Interactive movement

The Explorer fork switches accent acrylic to an opaque neutral tint during the native modal move/size loop, then restores the current opacity and tint on exit. It changes composition policy only at loop boundaries, not on each position notification. Settings updates preserve the temporary drag state; disabling the effect and destroying the window remove its subclass. Owned-window regressions exercise these lifecycle paths and verify that the actual compositor accepts the policy requests. They do not measure live Explorer frame rate or establish that all possible drag stalls are eliminated.
