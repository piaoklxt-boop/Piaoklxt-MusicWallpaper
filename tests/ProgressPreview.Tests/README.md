# Progress preview regressions

This Windows-only console test runs the real `ProgressPreviewForm` using only a
window created by the test process outside the complete virtual desktop. It does
not install a hook, interact with QQ Music or Explorer, or change the foreground
window. No monitor resolution, device name or machine-specific path is assumed.

The original eleven checks cover timeline bounds, foreground preservation, layered and
input-transparent styles, hit testing, non-activation, rendered thumb positions,
pointer updates from another thread, UI-timer coalescing and timer shutdown.
Additional visual checks use synthetic colored and light gradients with an old
thumb painted into them. They verify background restoration, old-thumb removal,
thin-rail and small-thumb dimensions, endpoint clamping, one backdrop per gesture,
and a transparent fallback. The antialiased rail may have one fringe row; the
color-key fallback must have no colored fringe. No real screen pixels are sampled.

Painting is checked in memory with `DrawToBitmap`. No images are saved by default.
An optional output-directory argument saves four synthetic before/after PNGs for
visual review. The fallback image contains the transparency-key RGB in transparent
areas because `DrawToBitmap` does not convert the window color key to PNG alpha.

```powershell
dotnet build .\tests\ProgressPreview.Tests\ProgressPreview.Tests.csproj -c Release -o .\artifacts\tests\progress-preview '-bl:artifacts\progress-preview-{}.binlog'
& .\artifacts\tests\progress-preview\ProgressPreview.Tests.exe
# Optional synthetic visual artifacts:
& .\artifacts\tests\progress-preview\ProgressPreview.Tests.exe .\artifacts\preview-visuals
```

A failed assertion returns exit code 1. These tests cover the preview itself;
the controller still owns gesture lifetime and the single seek sent on release.
The suite also reports one warmed-up restoration timing each for 1920x18 and
3840x18 synthetic images; those measurements are informational, not timing gates.
