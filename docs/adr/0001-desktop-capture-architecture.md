# Use a capture mirror instead of reparenting QQ Music

QQ Music repeatedly resets its parent and uses `WS_EX_NOREDIRECTIONBITMAP`, so directly reparenting its `TXGuiFoundation` window into WorkerW is unstable and ordinary capture initially returns blank frames. The wallpaper therefore keeps QQ Music as a bottom-Z-order top-level source window at the current desktop work-area geometry, temporarily enables DWM redirection, captures it with Windows Graphics Capture, and renders the mirror in the Windows 11 raised-desktop WorkerW. The controller preserves desktop-icon priority and forwards mapped pointer messages without presenting or focusing the source window.

The trade-off is a second GPU surface and more recovery logic, accepted because it preserves QQ Music's own animated lyric UI, remains compatible with taskbar restoration, and avoids fighting the application's parent-reset loop.

Controls whose native pop-ups cannot be represented reliably in the mirror use controller-owned overlays. The volume overlay writes to QQ Music's audio session, while the playback-order overlay opens and selects QQ Music's native menu in the offscreen source. The comment action remains disabled because its separate native surface cannot be mirrored without changing focus and desktop ownership.

Explorer accent styling is optional and runs in a separate bundled GPLv3 process. The controller starts the portable runtime only when no external Windhawk instance is active, and keeps the wallpaper process and its MIT license independent from the Explorer injection component.
