# Agent lessons from desktop interaction work

This is the project record of approaches that failed during development. It contains no machine-specific diagnostics; each lesson states the observable failure, root cause, durable correction, and regression guard.

## v1.2.1 audit and acceptance corrections

### Hit testing and invocation used different comment selectors

- **Failure:** The repeated-player-bar regression selected the active comment hit region, but actual invocation could still choose the deeper stale control.
- **Why it failed:** The test covered the bottommost selector in isolation; the production invocation path used a different general selector.
- **Correction:** Share active-launcher selection between hit testing and dispatch. Carry the selected comment map's snapshot through the queued action so another scan cannot substitute a different execution snapshot.
- **Guardrail:** `ControllerRegression.Tests` exercises the real controller dispatch against owned offscreen windows, including duplicate and absent launchers. A passing selector-only test is not evidence that its caller uses it.

### Successful source parking repeatedly expired the accessibility cache

- **Failure:** A nominal idle refresh interval was bypassed by the maintenance loop. An initial correction still treated unchanged parking as recovery.
- **Why it failed:** First, any successful `SetWindowPos` invalidated the cache. Then the check compared the requested original style with the style Windows actually maintained after `ShowWindow`, so normalized maximized state appeared to change every time.
- **Correction:** Compare observed geometry and styles before and after parking, together with actual hidden/minimized/presented recovery. Preserve the deadline for a successful no-op and discard a scan captured across a geometry change.
- **Guardrail:** Controller regressions cover repeated parking, original maximized/minimized styles, movement and hidden-window recovery. Runtime scan counts verify the schedule in addition to testing the interval constant.

### A watchdog warning was treated as proof of hook removal

- **Failure:** Repeated recovery logs asserted that the hook had been dropped and suggested a timeout without measuring callback duration.
- **Why it failed:** A delayed maintenance poll can see movement already delivered by the hook, followed by an old callback timestamp after the cursor stops. The previous predicate treated that healthy sequence as a missed event.
- **Correction:** Compare the current cursor with the last delivered callback position before recovery. Describe remaining gaps neutrally and collect optional callback timing, active-stage and scan-count diagnostics without writing logs on every hook event.
- **Guardrail:** `MouseHookWatchdog.Tests` checks delayed-poll suppression, genuine unobserved movement, initial unknown position and timeout boundaries. Passing these cases fixes the demonstrated false positive; it does not prove that every historical gap had the same cause.

### First comment clicks could miss the accessory-window path

- **Failure:** A first comment click could appear inert while subsequent clicks opened the window. The original observation did not establish one exclusive cause.
- **Why it failed:** The routing audit exposed two gaps: a generic semantic comment hit only posted to the source control, and the specialized hit map could lag behind available controls. Separate publication of hit regions and execution snapshots also allowed mismatched data.
- **Correction:** Promote an exact active comment hit to the accessory-window lifecycle. Associate its bounds with its snapshot. On a cache miss, retain Explorer's event and perform a bounded asynchronous semantic probe, after transient dismissal has finished. Pass fresh data and request-validity checks through invocation, presentation and retry.
- **Guardrail:** `SemanticClickResolver.Tests` rejects stale duplicate bars, unrelated controls, expired or superseded clicks, replaced windows and changed geometry. Runtime guards also cancel when the cursor leaves or desktop ownership changes. Keep the full-tree probe off the hook, and include it in scan diagnostics.

### Missing drag feedback was mistaken for a failed seek command

- **Failure:** The user described the timeline as not dragging, although release already reached the requested playback position.
- **Why it failed:** A successful seek-on-release contract had been treated as a complete drag experience. It provided no visible pending position during the gesture.
- **Correction:** Separate command delivery from visual feedback. Keep the single seek on release and add a purely visual, non-activating, click-through preview using the timeline captured at gesture start. Coalesce pointer changes on the UI thread while the hook only updates the pending position.
- **Guardrail:** Preserve the existing seek-policy tests and add `ProgressPreview.Tests` for bounds, pointer feedback, non-activation, input transparency and timer lifetime. Confirm whether a reported drag failure means missing feedback or an incorrect final playback position before changing input dispatch.

### A technically correct preview still looked like an unrelated overlay

- **Failure:** The first preview followed the pointer, but its dark rectangle and oversized thumb looked out of place on the player skin.
- **Why it failed:** Input and window tests checked behavior but did not assess the preview against the surrounding visual surface.
- **Correction:** Sample the narrow timeline backdrop once before showing the preview, reconstruct its background around the old track and thumb, and draw a thin rail with a small thumb. Use a transparent color-key fallback when capture is unavailable, with edge rendering that avoids a colored fringe.
- **Guardrail:** Synthetic light and colored backdrops test old-thumb removal, rail/thumb size, endpoints, capture frequency and fallback rendering. Inspect the rendered examples and obtain appearance acceptance as well as behavioral acceptance. Backdrop reconstruction is a once-per-gesture UI cost, never work for the mouse hook.

### Test totals were too broad a description of acceptance

- **Failure:** Earlier successful policy checks coexisted with production routing defects and unsatisfactory visual feedback.
- **Why it failed:** Pure policy decisions, real controller methods exercised against test windows, synthetic rendering and live player behavior were treated as interchangeable evidence.
- **Correction:** Run all five suites and the playback-mode contract script, state what each covers, then validate the changed behavior and appearance in the final candidate. Preserve that candidate's identity through packaging and installation.
- **Guardrail:** CI executes the five suites before packaging. The controller and preview test READMEs define their offscreen-fixture and synthetic-image limits. Assertion counts and a startup smoke test are not a complete live QQ Music interaction matrix.

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

### Global input callbacks performed synchronous Explorer queries

- **Failure:** Every desktop button press could wait for a cross-process list-view hit test. Managed exceptions could also leave the native callback without a containment boundary.
- **Correction:** Contain callback failures, release the event and defer diagnostics through one bounded error slot. Read desktop icon bounds in one background accessibility scan; invalidate on Explorer geometry events and reject expired or replaced-window snapshots. Unknown ownership remains with Explorer. Ignore the hidden label editor exposed alongside real list items.
- **Guardrail:** Controller tests exercise the real callback and icon guard, blocked and invalidated scans, and a real offscreen ListView with a hidden editor. A read-only Explorer probe establishes local capture compatibility; it does not establish all live player interactions or prove the cause of historical hook gaps.

### A valid cached HWND was treated as sufficient window identity

- **Failure:** A cached source handle with a mismatched process identity could still be parked or restored. Window enumeration also left Process objects undisposed and missed process-exit exceptions.
- **Correction:** Record the source PID with its captured state and recheck it before window mutation or input mapping. Share a disposing process-name lookup that treats process exit and access failure as unavailable.
- **Guardrail:** Owned-window regressions reject mismatched source identity without changing styles or geometry. Process-query regressions exercise failure and disposal. PID checks reduce stale-handle risk; they are not atomic with later Win32 calls.

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

### Independent comment retries escaped ordinary popup cancellation

- **Failure:** Comments used a separate polling loop and could replay a toggle while waiting for a delayed window; cancellation and tracked input bounds were managed separately from other native popups.
- **Correction:** Keep discovery, presentation, request validity and tracked surfaces in `AccessoryWindowCoordinator`. Comments share the original click/request identity and perform one control invocation. Repeated discovery is permitted; repeated toggle clicks are not.
- **Guardrail:** Controller fixtures exercise delayed comments, explicit cancellation without a newer click, moved popup bounds, scoped recovery and ordinary popup regressions through the shared implementation.

### Unknown windows and ordinary closure were ambiguous diagnostic outcomes

- **Failure:** Selecting the first new window can guess the wrong input owner, while treating every discovery deadline as a failure mislabels a successfully closed popup.
- **Correction:** Reject unknown or tied candidates; preserve the full discovery deadline for a possible replacement window, then recognize closure of previously tracked matching windows as `Closed`.
- **Guardrail:** Native-window tests cover ambiguity, unknown titles and toggle-close without resurrection or a timeout failure. The diagnostic buffer accepts only enum/numeric fields, is bounded to 32 entries and never exports old logs or window titles.

### Display names replaced stable launcher identity

- **Failure:** Quality renamed its visible label to accompaniment, so a quality hit dispatched to the separate accompaniment launcher. Overlapping old player-bar nodes could also replace the hit target during invocation.
- **Why it failed:** Hit testing matched either name or description, while invocation independently preferred a description match and a deeper node.
- **Correction:** Classify popup launchers by description, falling back to name only when description is absent. Publish one immutable map containing the selected node, source geometry and hit bounds, then dispatch that map. Within overlapping duplicate nodes, prefer the active lower player bar.
- **Guardrail:** Controller regressions record real posted messages for renamed labels, duplicate bars, snapshot replacement and changed source geometry. Clear the retained transient launcher when switching to other menu lifecycles.

### Source maintenance demoted an owned popup

- **Failure:** A popup remained visible and tracked but moved below other windows at the next source-maintenance tick.
- **Why it failed:** Repeated `HWND_BOTTOM` parking changed the source's owned-window ordering, even when its geometry and non-topmost state had not changed.
- **Correction:** Preserve owner ordering during source parking with `SWP_NOOWNERZORDER`, while retaining non-activation and the source's offscreen position.
- **Guardrail:** Owned offscreen-window regressions compare both the popup's topmost style and its order relative to an unrelated window; an unowned popup is the control case. This reproduces the mechanism, not the identity of a historical window that has already closed.

### Discovery ended before the actual panel was available

- **Failure:** A reused visible panel, a panel appearing after one second, or a real dialog created after its shadow was not published as an input surface.
- **Why it failed:** Discovery accepted the first newly visible same-process window, including the shadow, and stopped after about one second.
- **Correction:** Filter native popup classes and known titles, exclude shadows and unrelated known panels, recover matching visible panels not already published, and allow a bounded five-second discovery period. Retain the original request identity across click delivery and discovery; newer clicks, dismissal and source changes cancel it.
- **Guardrail:** Controller regressions cover new, reused, delayed and shadow-first windows, cancellation during click delivery, superseded requests, source movement and toggle-close without resurrection. Unknown newly visible QQ panels retain a compatibility fallback; live panel identity and first-load behavior still require real-player validation.

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

### CI parsed UTF-8 scripts with a legacy code page

- **Failure:** The playback-mode contract script passed locally but failed to parse in CI around its Chinese mode names.
- **Why it failed:** The workflow selected Windows PowerShell 5.1, which can read UTF-8 scripts without a BOM using the system ANSI code page. The contract also compared file line endings with the host's newline sequence.
- **Correction:** Use PowerShell 7 (`pwsh`) consistently in CI, read source text explicitly as UTF-8, and normalize CRLF/LF before exact contract comparisons.
- **Guardrail:** Run the same contract against UTF-8 fixtures using both LF and CRLF, retaining the positive mode mappings and the negative parameterless-command and coordinate-routing checks.

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
