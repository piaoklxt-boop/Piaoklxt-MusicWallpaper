# Controller regressions

This Windows-only console harness compiles the real controller source and exercises
its semantic action dispatch and source-parking methods. It creates only its own
offscreen native windows and records posted input in their window procedures.
It does not run the controller constructor, install a mouse hook, start QQ Music,
or change the desktop/theme. A process-ownership check guards every fixture HWND.

The regressions cover duplicate comment launchers, absent launchers, repeated
source parking (including maximized/minimized original style snapshots), source
movement and hidden-window recovery. Unlike the pure
policy suite, they exercise the actual controller call paths that select a
target and invalidate the accessibility cache. They do not replace live QQ Music
acceptance testing or prove compatibility with all player versions.

```powershell
dotnet build .\tests\ControllerRegression.Tests\ControllerRegression.Tests.csproj -c Release --artifacts-path .\artifacts\controller-regressions '-bl:artifacts\controller-regressions-{}.binlog'
& .\artifacts\controller-regressions\bin\ControllerRegression.Tests\release\ControllerRegression.Tests.exe
```

The executable returns a nonzero exit code on any failed regression. It fails
safely if the display arrangement provides no offscreen test parking rectangle.
