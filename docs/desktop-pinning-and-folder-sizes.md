# Virtual desktops and folder sizes

The source QQ Music window is pinned automatically when captured and again after
source-window/process replacement. A single queued worker rechecks at most once
per 30 seconds after success (five seconds after an unavailable interface). It
checks the HWND's owning process, source generation and cancellation before
dispatch. Repeated maintenance does not create renderers or switch desktops.
Existing manual pins remain intact, including when wallpaper is paused.

The Shell view-pinning interface is undocumented: it is capability-probed and
failures are logged as unavailable. The public virtual-desktop manager alone
does not expose pinning. Interface prefixes were checked against Microsoft's
[TypeAgent implementation](https://github.com/microsoft/TypeAgent/blob/main/dotnet/autoShell/Services/WindowsVirtualDesktopService.cs).
No version-specific desktop-manager vtables are called. Windows updates still
require compatibility validation.

Folder sizes use the separate GPL Windhawk component in the existing runtime.
They appear in Explorer's existing Size column; the independent switch is on
the Explorer control-panel page. Turning it off cancels queued work without
restarting the runtime or unloading the Explorer/taskbar XAML modules.

Explorer's property path only resolves an item's parsing name and queries an
in-memory cache. Metadata enumeration runs on one thread in Windows background
mode. Complete values are cached for two minutes, with at most 512 cache entries
and 128 pending requests. Completion sends an item update notification. Cache
misses and incomplete results are blank, never guessed as zero. Large directories
therefore fill in later; cached values may lag file changes until queried again
after expiry. Sorting uses available sizes and may need a refresh after a batch
finishes.

Only local fixed drives are traversed. Directory reparse points are excluded to
avoid cycles and double counting; network/removable paths, offline directories,
inaccessible subtrees and aborted scans stay unknown. A scan stops after five
minutes, two million entries or 100,000 queued subdirectories. File contents are
never read. The implementation adds no whole-disk indexer or Everything dependency.

Owned offscreen-window tests cover real Shell pin/unpin and lifecycle dispatch.
Native folder tests cover nested/empty sizes, unknown versus zero, cancellation,
cache reuse, nonblocking property requests and worker shutdown. Live QQ Music
restart and real Explorer-column rendering remain separate acceptance checks.

The standard controller suite uses injected Shell operations for lifecycle tests.
Run `ControllerRegression.Tests.exe --desktop-pin-live` separately on an interactive
Windows desktop to exercise real pin/unpin on an owned offscreen test window.
This check is deliberately excluded from hosted CI, where a Shell desktop may
not exist. `tests/FolderSizes.Tests/FolderSizes.Tests.vcxproj` runs the native cache
checks with MSVC in CI; `scripts/build-folder-sizes.ps1` also runs them while
building the bundled module with the Windhawk toolchain.
