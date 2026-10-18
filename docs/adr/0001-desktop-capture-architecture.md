# Use a capture mirror instead of reparenting QQ Music

QQ Music repeatedly resets its parent and uses `WS_EX_NOREDIRECTIONBITMAP`, so directly reparenting its `TXGuiFoundation` window into WorkerW is unstable and ordinary capture initially returns blank frames. The wallpaper therefore keeps QQ Music as a top-level source window, temporarily enables DWM redirection, captures it with Windows Graphics Capture, and renders the mirror in the Windows 11 raised-desktop WorkerW; a small controller preserves desktop-icon priority and briefly routes real pointer input to the source window.

The trade-off is a second GPU surface and more recovery logic, accepted because it preserves QQ Music's own animated lyric UI, remains compatible with taskbar restoration, and avoids fighting the application's parent-reset loop.
