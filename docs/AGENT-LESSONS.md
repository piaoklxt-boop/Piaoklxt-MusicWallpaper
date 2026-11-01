# Agent lessons from the v1.2.0 interaction work

This is the project record of approaches that failed during development. It contains no machine-specific diagnostics; each lesson states the observable failure, root cause, durable correction, and regression guard.

## Scope and validation mistakes

### Front-end automation was confused with project implementation

- **Failure:** Early work treated a file/code-level request as foreground UI automation and explored direct screen interaction.
- **Why it failed:** Operating the visible application does not repair the controller's routing model and cannot produce a reproducible release.
- **Correction:** Inspect and change project files, native-window ownership, and accessibility controls. Use probes only to establish facts, never as the product interaction path.
- **Guardrail:** Every shipped behavior must be represented in source, tests, and release artifacts.

### “All controls are control-level” was claimed before the full control inventory was verified

- **Failure:** Some actions still depended on broad or inferred hit regions while the implementation was described as complete.
- **Why it failed:** One unresolved fallback could steal input from a neighboring control or break after a skin change.
- **Correction:** Inventory every exposed action and require semantic-control discovery. The timeline may be derived only from live time-label controls because QQ Music exposes no slider.
- **Guardrail:** Regression tests reject an undiscovered media control fallback and cover every special action.

### Fixes were validated individually but not as an interaction matrix

- **Failure:** Repairing one action repeatedly regressed comments, playback order, popup dismissal, or wheel ownership.
- **Why it failed:** The controls share input hooks, cached accessibility data, and transient-window state.
- **Correction:** Test ownership, invocation, interaction, dismissal, and recovery across main surfaces, popups, accessory windows, and overlays.
- **Guardrail:** `DesktopInputPolicy.Tests` contains cross-control overlap, lifecycle, duplicate-tree, and scan-policy cases.

## Input-routing mistakes

### Broad coordinate hit targets covered the progress bar

- **Failure:** The bottom play area was too large, so timeline clicks toggled playback or never reached seeking.
- **Why it failed:** A fixed visual estimate was treated as a control boundary.
- **Correction:** Use live semantic bounds for play, previous, and next; do not guess a missing control's position.
- **Guardrail:** Tests assert that progress does not trigger play/pause and that undiscovered controls have no coordinate fallback.

### Progress dragging sent repeated seeks

- **Failure:** Music audibly accelerated or jumped repeatedly while the pointer moved.
- **Why it failed:** QQ Music treats each forwarded click pair as an immediate seek rather than a native continuous drag.
- **Correction:** Track the pending point locally and submit exactly one mapped seek on mouse-up.
- **Guardrail:** Tests require down to start tracking, move to update only, and up to commit once.

### The mouse hook consumed held movement

- **Failure:** Hook coordinates changed while the visible cursor appeared frozen or later jumped.
- **Why it failed:** Consuming move events prevented Windows from updating the real cursor.
- **Correction:** Leave held mouse movement available to Windows while recording the pending seek point.
- **Guardrail:** The progress-move policy must return control to the system cursor.

### Input was sent to the wrong QQ Music window layer

- **Failure:** A mapped drag or click could not reproduce native behavior even though the top-level coordinates looked correct.
- **Why it failed:** The top-level player and rendered browser child use different coordinate spaces and input ownership.
- **Correction:** Resolve the actual rendered child or popup surface before mapping and dispatching input.
- **Guardrail:** Popup and timeline tests resolve against the owning surface's control tree.

### Main-surface cancellation used Escape

- **Failure:** Clicking controls or dismissing a temporary UI returned QQ Music to its home page instead of leaving the lyric/player page intact.
- **Why it failed:** Escape is navigation on the main surface, not merely popup dismissal.
- **Correction:** Use control/window lifecycle messages for main-surface transient UI; use Escape only for an independent popup that owns it.
- **Guardrail:** Tests distinguish main-surface cancellation from independent-popup cancellation.

## Popup and accessory-window mistakes

### Comments initially brought the source player forward

- **Failure:** The main QQ Music interface covered the desktop or flashed its taskbar button when comments opened.
- **Why it failed:** The launcher path activated or presented the source player instead of invoking it in the background.
- **Correction:** Invoke the semantic comment control without foreground activation, then restore only the separate comment accessory window.
- **Guardrail:** Tests require comment launch to keep the main player behind the desktop.

### Comment windows were not recovered or dismissed reliably

- **Failure:** Comments could open offscreen, appear behind the mirror, or remain after a blank desktop click.
- **Why it failed:** The accessory window was treated as part of the source player and its independent bounds/lifecycle were ignored.
- **Correction:** Detect, reposition, publish, and explicitly hide the accessory window while leaving the source player untouched.
- **Guardrail:** Tests cover working-area restoration and blank-click dismissal without affecting desktop icons.

### Popup wheel and click events leaked into lyrics

- **Failure:** Scrolling comments or a playlist scrolled the lyric view; popup controls looked visible but did not react.
- **Why it failed:** Routing continued through the main mirror after a native popup became the top interaction owner.
- **Correction:** While a popup is open, resolve its child surface and give it exclusive click/wheel ownership.
- **Guardrail:** Tests require popup-owned wheel input even when the popup exposes no child controls.

### Repeated accessibility scans made popups feel blocked

- **Failure:** Comments, quality, accompaniment, and other popup actions opened slowly and reacted late.
- **Why it failed:** Full main-tree capture was repeated during popup interaction and blocked QQ Music's UI thread.
- **Correction:** Cache stable semantic controls, pause main-tree scans while native popups are active, and reuse the popup surface.
- **Guardrail:** Tests cover active-popup scan suppression and a bounded idle refresh policy.

### Playback order waited for a top-level popup that never existed

- **Failure:** The playback-order button appeared inert.
- **Why it failed:** QQ Music renders its mode menu inside the main surface, so waiting for a new top-level window could never complete.
- **Correction:** Use a controller-owned native button picker and apply explicit `/playmode 1..4` values.
- **Guardrail:** The playback-mode contract test rejects pointer-coordinate selection and parameterless mode commands.

### Shared bounds and duplicate controls selected stale launchers

- **Failure:** Playback-order choices replaced the launcher target, or comments stopped opening after the source player moved.
- **Why it failed:** QQ Music can expose menu choices at one shared bound and retain duplicate bottom player bars in the accessibility tree.
- **Correction:** Filter semantic conflicts, select the active control group inside current source bounds, and invalidate mappings after every source move or recovery.
- **Guardrail:** Tests cover shared-bound playback choices, duplicate comment launchers, active timeline labels, and stale offsets.

## Release-process mistakes

### Ad hoc deployment accumulated indistinguishable local binaries

- **Failure:** Many per-commit backups remained beside the active executable, making the installed version ambiguous.
- **Why it failed:** Testing copies were treated as releases without one tagged artifact as the source of truth.
- **Correction:** Build one versioned portable ZIP, publish it from an immutable tag, then install that release artifact.
- **Guardrail:** Release completion requires matching version constants, changelog entry, tag, release asset, and installed binary hash.

### User and agent documentation lagged behind behavior

- **Failure:** Documentation still described fixed-coordinate fallbacks or unsupported comments after the code and tests had changed.
- **Why it failed:** Behavior changes were committed faster than their project model and release notes.
- **Correction:** Update the changelog, README, glossary, ADRs, and agent guidance in the release commit.
- **Guardrail:** Search for the prior version and obsolete behavior claims before tagging.
