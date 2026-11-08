# Controller regressions

This Windows-only console harness compiles the real controller source and exercises
its semantic action dispatch and source-parking methods. It creates only its own
offscreen native windows and records posted input in their window procedures.
It does not run the controller constructor, install a mouse hook, start QQ Music,
or change the desktop/theme. A process-ownership check guards every fixture HWND.

The regressions cover duplicate comment launchers, absent launchers, repeated
source parking (including maximized/minimized original style snapshots), source
movement and hidden-window recovery. Popup cases also cover renamed quality
labels, overlapping player bars, retaining the hit target through queued dispatch,
cross-menu dismissal and cancellation during the actual posted click. Native
window fixtures exercise owned-popup ordering, reused and delayed panels,
shadow-first creation, toggle-close, and cancellation by newer requests or source
movement. Fixtures use their own registered QQ-style window classes; no production
QQ process is inspected or controlled. Unlike the pure
policy suite, they exercise the actual controller call paths that select a
target and invalidate the accessibility cache. They do not replace live QQ Music
acceptance testing or prove compatibility with all player versions.

The coordinator tests additionally cover moved input surfaces, ambiguous/unknown
windows, shared comment cancellation and single-dispatch delayed comments.
Diagnostic regressions cover the 32-entry bound, allowlisted export schema,
concurrent recording, quiet cancellation, consecutive failures and notification
cooldown. Export tests inspect the returned text without opening a save dialog.

```powershell
dotnet build .\tests\ControllerRegression.Tests\ControllerRegression.Tests.csproj -c Release --artifacts-path .\artifacts\controller-regressions '-bl:artifacts\controller-regressions-{}.binlog'
& .\artifacts\controller-regressions\bin\ControllerRegression.Tests\release\ControllerRegression.Tests.exe
```

The executable returns a nonzero exit code on any failed regression. It fails
safely if the display arrangement provides no offscreen test parking rectangle.

Shared-slot regressions replay visible accompaniment/hidden translation and the reverse, checking both hit classification and actual posted control messages. An owned offscreen accessory verifies that blank mirror space is not registered as a source control; hidden controls without a visible replacement retain the parked-source fallback.

Untitled-panel regressions run the production discovery loop with owned offscreen HWNDs and an injected semantic snapshot matching the observed accompaniment panel. They cover both launchers, bounded capture, owner/class/feature rejection, cancellation during capture, anchor positioning, maintenance and toggle-close. The injected MSAA read does not claim live QQ Music acceptance.

Placement lifecycle tests simulate a native layout reset after successful presentation and a second launcher reusing the tracked HWND. They verify corrected input bounds, no additional semantic scan, cancellation on a newer click, no hidden-window resurrection and expiry that permits later movement.

Layered-panel regression initializes an owned offscreen layered fixture, then checks that the production positioning path paints or leaves an invalid update region. It verifies the repaint request, not QQ Music's compositor output. Live screenshot acceptance is separate.

The executable accepts an optional case-insensitive test-name substring (for example, `ControllerRegression.Tests.exe redraw`). No argument runs every case; a filter matching no tests returns exit code 2.

Input-safety regressions call the real mouse callback with a failing payload,
exercise the controller's desktop-icon guard, and verify that a blocked background
scan cannot block that guard or publish data after invalidation. Icon fixtures
cover empty space, expired data, window movement/replacement, real ListView MSAA
and hidden label editors. These tests use owned windows and synthetic icon bounds;
they do not install a global mouse hook. Process-identity tests cover unavailable
processes, access errors, disposal and source-PID mismatch before window mutation.
Wheel regressions verify screen-space coordinates, signed deltas and explicit
delivery failure for invalid geometry, missing popups and stale source identity.

Player-style fixtures reproduce an unowned untitled panel and require a heading,
multiple distinct style choices and a selection button before presentation.
Quality fixtures retain the native arrow offset through source-to-desktop mapping
and delayed resize; they do not infer the arrow from a fixed pixel inset.
