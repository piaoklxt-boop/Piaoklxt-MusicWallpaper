# Whole-window interaction and shell appearance candidate

This development candidate builds on v1.3.0. It is not a newly published release.

## Evidence

- Controller regressions: 96 passed in the final complete run. Two earlier popup fixture foreground assertions failed intermittently; both passed individually and in the final complete run. This does not establish absence of live activation defects.
- Desktop input policy, mouse-hook watchdog, semantic-click resolver, progress-preview and playback-order checks passed. Existing lyric/progress routing remains in its original paths.
- A read-only snapshot of the current QQ Music page exposed named navigation panes and a readable text control. Inactive ancestors accounted for many stale descendants. No automated clicks or text writes were sent to QQ Music during validation.
- Text value read/write, replacement rejection, disabled controls, deep traversal and delayed navigation were exercised through the production routing/capture functions with isolated fixtures. Real QQ Music value-write support still needs user acceptance.
- The Release controller, renderer and portable package built successfully. The taskbar mod was compiled from the pinned, unmodified upstream source. Control-panel pages were rendered and visually checked.
- After local installation, first-frame capture and source geometry checks passed. The live renderer's bottom extension changed its height from the working-area height to the monitor height and restored it on disable, while preserving width. Preferences were restored after this check.

## Limits and remaining acceptance

The whole QQ Music source window is captured. Interaction covers provider-exposed navigation panes, list items (including native double-click), links, buttons and writable text controls. It does not promise every custom-drawn surface, arbitrary gestures or unknown native dialogs. Generic dialogs require the exact semantic title and source ownership described in ADR 0003.

Taskbar acrylic is opt-in. Its live appearance and unified palette need user acceptance on the target Windows taskbar; successful compilation or window geometry alone does not verify visual blur. Extension is limited to the existing primary-monitor wallpaper surface.

An Explorer XAML crash during installation recreated the desktop host while the renderer process remained alive. The new taskbar mod was disabled at that time. Restarting only the renderer restored the wallpaper through existing recovery. The cause of that crash and automatic recovery after host destruction were deliberately left outside this feature task.
