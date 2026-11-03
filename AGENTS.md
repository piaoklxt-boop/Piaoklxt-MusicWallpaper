# Project agent guide

Preserve the capture-first architecture in [ADR 0001](docs/adr/0001-desktop-capture-architecture.md) and the control-level routing decision in [ADR 0003](docs/adr/0003-control-level-input-routing.md). Use the canonical terms in [GLOSSARY.md](GLOSSARY.md).

## v1.2.1 baseline

- Desktop seeking supports click, drag, and exposed lyric timestamps. A drag previews the pending position and commits once on release while the real cursor keeps moving.
- Comments use the same semantic snapshot for hit testing and invocation. A generic comment hit follows the accessory-window lifecycle; a cache miss may perform a bounded asynchronous semantic probe while Explorer retains the original event.
- Translation, accompaniment, player style, quality, effects, more-menu, and playlist actions route through their semantic controls and popup surfaces.
- Playback order uses a controller overlay with native buttons and explicit QQ Music mode values.
- Accessibility snapshots preserve their idle deadline during unchanged source parking, pause while a native popup owns input, and invalidate after actual source movement or recovery.
- Duplicate and shared-bound accessibility nodes are filtered so stale controls cannot replace the active launcher or timeline group.
- Hook recovery distinguishes an unobserved cursor position from a delayed poll of already delivered movement. Callback-gap diagnostics describe observations rather than asserting an operating-system timeout.

## Interaction invariants

- `AccessoryWindowCoordinator` owns native window discovery, request validity, presentation, input-surface tracking and dismissal. Route both comments and ordinary popups through it; do not reintroduce controller-owned retry loops or surface arrays. Poll for delayed windows without replaying toggle clicks.
- Unknown or equally plausible popup candidates remain unpresented and produce a diagnostic reason. Refresh published input bounds when an accessory moves; recovery may dismiss only tracked surfaces and the identified comment accessory.
- `InteractionDiagnostics` stores at most 32 routed interactions in memory using enum/numeric fields. Manual export must not include raw application logs, window text, song/lyric/account text, paths or screenshots. Single cancellation stays quiet; three consecutive failures may notify with a one-minute cooldown. Recovery must not restart QQ Music or issue playback commands.

- Treat the desktop mirror, source player, accessory windows, native popups, and controller overlays as different input owners.
- The progress preview is a visual surface, not an input-owning controller overlay. It remains non-activating and click-through; the existing hook owns the seek gesture. Sample its skin backdrop once per gesture on the UI thread and coalesce pointer updates there.
- Resolve QQ Music actions from fresh semantic controls and their live bounds. A source-window move or recovery invalidates cached mappings; duplicate player bars must be filtered to the active group.
- Keep desktop icons and filename editing owned by Explorer. Route only rendered-player areas to QQ Music, without presenting or focusing the source player.
- Derive the timeline from the active current/total-time controls. A seek gesture leaves real cursor movement to Windows and commits one seek on release.
- Give an open native popup its own click and wheel routing. Close source-player transient UI through its control lifecycle; reserve Escape for independent popup surfaces.
- Treat comments as an accessory window: invoke its semantic launcher, restore that window into the work area, and dismiss it on a blank desktop click. Preserve the selected snapshot through invocation; cancel delayed probes and their retries when their click, source geometry, cursor or validity deadline changes.
- Keep controller overlays limited to volume and playback order. Playback order uses native buttons and explicit `/playmode 1..4` values.

## Change workflow

1. Identify whether the failure concerns command delivery, gesture feedback, window ownership or appearance, then reproduce that symptom before changing routing.
2. Add a regression at the affected production call path. Pure selector tests supplement controller dispatch tests; an interval constant alone does not test the maintenance loop.
3. Run all five suites: `DesktopInputPolicy.Tests`, `ControllerRegression.Tests`, `MouseHookWatchdog.Tests`, `SemanticClickResolver.Tests` and `ProgressPreview.Tests`, plus `scripts/test-playback-mode-command.ps1`. Use the commands in `.github/workflows/build.yml` and retain build logs. Build the Release controller and portable package before publishing.
4. Record the bounds of validation: policy assertions, owned offscreen-window tests and synthetic visual tests cover different behavior. A passing count or startup smoke test does not establish live QQ Music compatibility. Verify the changed interaction and appearance in the final candidate before declaring acceptance.
5. Audit changed documentation and artifacts for machine-specific paths, usernames, process identifiers, logs, and diagnostic captures. Match the accepted candidate, version, tag, release asset and installed hashes when completing a release.

Before changing input routing, popup behavior, accessibility caching, hook recovery, progress feedback or release packaging, read the relevant failure and regression guard in [Agent lessons](docs/AGENT-LESSONS.md). User-facing changes belong in [CHANGELOG.md](CHANGELOG.md).
