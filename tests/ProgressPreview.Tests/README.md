# Progress preview regressions

This Windows-only console test runs the real `ProgressPreviewForm` using only a
window created by the test process outside the complete virtual desktop. It does
not install a hook, interact with QQ Music or Explorer, or change the foreground
window. No monitor resolution, device name or machine-specific path is assumed.

Eleven checks cover timeline bounds, foreground preservation, layered and
input-transparent styles, hit testing, non-activation, rendered thumb positions,
pointer updates from another thread, UI-timer coalescing and timer shutdown.
Painting is checked in memory with `DrawToBitmap`; no screenshots are saved.

```powershell
dotnet build .\tests\ProgressPreview.Tests\ProgressPreview.Tests.csproj -c Release -o .\artifacts\tests\progress-preview '-bl:artifacts\progress-preview-{}.binlog'
& .\artifacts\tests\progress-preview\ProgressPreview.Tests.exe
```

A failed assertion returns exit code 1. These tests cover the preview itself;
the controller still owns gesture lifetime and the single seek sent on release.
