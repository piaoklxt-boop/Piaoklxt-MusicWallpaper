# Project agent guide

Preserve the capture-first architecture in [ADR 0001](docs/adr/0001-desktop-capture-architecture.md) and the control-level routing decision in [ADR 0003](docs/adr/0003-control-level-input-routing.md). Use the canonical terms in [GLOSSARY.md](GLOSSARY.md).

## v1.2.0 baseline

- Desktop seeking supports click, drag, and exposed lyric timestamps; a drag commits once on release without freezing the cursor.
- Comments open as a usable accessory window and close on a blank desktop click without presenting the source player.
- Translation, accompaniment, player style, quality, effects, more-menu, and playlist actions route through their semantic controls and popup surfaces.
- Playback order uses a controller overlay with native buttons and explicit QQ Music mode values.
- Accessibility snapshots are cached for responsiveness, paused while a native popup owns input, and invalidated after source movement or recovery.
- Duplicate and shared-bound accessibility nodes are filtered so stale controls cannot replace the active launcher or timeline group.
- Regression coverage records these interaction contracts in `tests/DesktopInputPolicy.Tests` and `scripts/test-playback-mode-command.ps1`.

## Interaction invariants

- Treat the desktop mirror, source player, accessory windows, native popups, and controller overlays as different input owners.
- Resolve QQ Music actions from fresh semantic controls and their live bounds. A source-window move or recovery invalidates cached mappings; duplicate player bars must be filtered to the active group.
- Keep desktop icons and filename editing owned by Explorer. Route only rendered-player areas to QQ Music, without presenting or focusing the source player.
- Derive the timeline from the active current/total-time controls. A seek gesture leaves real cursor movement to Windows and commits one seek on release.
- Give an open native popup its own click and wheel routing. Close source-player transient UI through its control lifecycle; reserve Escape for independent popup surfaces.
- Treat comments as an accessory window: invoke its semantic launcher, restore that window into the work area, and dismiss it on a blank desktop click.
- Keep controller overlays limited to volume and playback order. Playback order uses native buttons and explicit `/playmode 1..4` values.

## Change workflow

1. Reproduce an interaction failure and identify the owning surface before changing routing.
2. Add or update a regression in `tests/DesktopInputPolicy.Tests/Program.cs`; use `scripts/test-playback-mode-command.ps1` for playback-mode contracts.
3. Run both regression suites, build the Release controller, then build the portable release before publishing.
4. Audit changed documentation and artifacts for machine-specific paths, usernames, process identifiers, logs, and diagnostic captures.

For prior failed approaches and their guardrails, read [Agent lessons](docs/AGENT-LESSONS.md) before changing input routing, popup behavior, accessibility caching, or release packaging. User-facing changes belong in [CHANGELOG.md](CHANGELOG.md).
