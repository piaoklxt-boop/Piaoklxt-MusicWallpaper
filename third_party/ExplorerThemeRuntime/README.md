# Explorer theme runtime

This directory contains the minimal runtime used by Piaoklxt-MusicWallpaper to style File Explorer. Named address-bar and command-bar XAML controls use the original 38% accent tint flattened over the dark inactive-window base into one opaque color. This avoids both focus-dependent backdrop colors and black backing panels between nested controls. The classic file, navigation and preview panes share an accent-acrylic background with a lighter neutral tint.

- Windhawk 1.7.3 runtime: GNU GPL v3, https://github.com/ramensoftware/windhawk/releases/tag/v1.7.3
- Windows 11 File Explorer Styler 1.7 by m417z, with the Piaoklxt tint and opaque-header modifications (1.7-piaoklxt.5): GNU GPL v3, https://github.com/ramensoftware/windhawk-mods/blob/main/mods/windows-11-file-explorer-styler.wh.cpp
- The corresponding Windhawk 1.7.3 source is in `licenses/windhawk-1.7.3-source.zip`.
- The exact mod source distributed with the binary is in `licenses/windows-11-file-explorer-styler.wh.cpp`.
- The GPL v3 text is in `licenses/GPL-3.0.txt`.

The runtime is an independent process and separate work aggregated with the MIT-licensed Piaoklxt-MusicWallpaper application. No Windhawk UI, compiler, updater, or service is included. Piaoklxt-MusicWallpaper starts it in portable mode with its tray icon hidden.

To rebuild the modified x64 mod, provide a separate extracted Windhawk 1.7.3 developer toolchain to `scripts/build-explorer-styler.ps1 -WindhawkPath <toolchain>`. The script retains compiler logs, emits the mod DLL, and tests the production header parser and opaque-color composition; it does not install or start Windhawk. The compiler and its UI must not be copied into the portable application.

The configuration uses `acrylicblur` (the upstream accent-acrylic path), rather than the focus-dependent system Mica backdrop. Its neutral tint is locally adjusted. The optional `OpaqueBase="True"` extension to `WindhawkBlur` produces an opaque composition color brush, with no backdrop sampling or blur, while retaining the upstream theme-resource change notifications. Existing blur rules are unchanged unless this option is enabled. Header rules match named XAML controls; no screen positions or input interception are added. Confirm both focused and unfocused appearance in Explorer: a successful compositor attachment or DWM property check alone does not establish that a background is visible through classic controls.

The controller generates the active configuration from the packaged seed plus separately saved user preferences. `piaoklxtAcrylicOpacity` (0–255), `piaoklxtAcrylicGray` (0–200), and `piaoklxtSidebarAcrylic` (0/1) configure the native paint path. Turning off navigation acrylic writes opaque alpha only for descendants of the native navigation control; file-view backgrounds retain their own rendering. Header rules are regenerated for the selected mode. Applying changes restarts only the bundled styling runtime on a worker thread, with rollback of the prior active configuration on failure; it does not restart Explorer or QQ Music.

Caption-hosting tab containers and generic content roots must not receive opaque background overrides: they obscure native window caption buttons and hover feedback. Navigation row backgrounds include zero-character ETO_OPAQUE clears; these preserve opaque alpha when navigation acrylic is disabled.
