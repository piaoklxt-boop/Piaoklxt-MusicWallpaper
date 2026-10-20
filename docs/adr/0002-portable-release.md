# Ship a self-contained portable release

## Status

Accepted.

## Decision

Each release contains the managed controller, native renderer, application icon, license, and notices in one x64 ZIP. Runtime state is stored under `%LOCALAPPDATA%\Piaoklxt-MusicWallpaper`; the former `%LOCALAPPDATA%\QQMusicWallpaper` state is imported on first run. Startup uses the current executable path in the current user's Run key. The source repository excludes SDK packages, compiler output, logs, screenshots, and local diagnostics.

## Consequences

Users can unzip and launch without installing development tools. Contributors still restore NuGet packages and build both components. Moving the extracted folder is supported: the next launch updates the startup entry to the new path.
