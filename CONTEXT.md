# Domain context

Canonical project terms are defined in [GLOSSARY.md](GLOSSARY.md). Architecture decisions are recorded under `docs/adr/`.

## Invariants

- QQ Music remains a separately installed application and is never distributed with this project.
- The desktop icons remain usable while the mirror is active.
- Wallpaper startup expands QQ Music's player/details page through its exposed accessible control and verifies the visible page state; screen coordinates are never fixed to one resolution.
- Explorer owns desktop icons and active filename editing; QQ Music owns blank rendered-player areas. Pointer clicks, drags, hover and lyrics-wheel input are mapped from the current desktop work area to the live QQ Music window and forwarded in the background, so desktop interaction never presents or focuses the source window.
- User settings and logs live outside the portable release folder.
- Desktop volume and playback-order pop-ups belong to the controller; their changes are applied to QQ Music in the background. Comments are an accessory window: hit testing and invocation share the selected semantic snapshot, and a generic semantic comment hit uses the same opening and presentation lifecycle. A blank desktop click hides that window while the main source remains behind the desktop.
- An unrecognized desktop click remains available to Explorer. Its optional comment probe runs outside the hook, requires an exact active comment target, and is bounded by request age, click sequence, source window and geometry, cursor proximity and desktop ownership. Those checks continue through delayed presentation and retry.
- Media buttons use their exposed accessible bounds when available. QQ Music does not expose the progress track as a slider, so the controller derives it from the current/total-time labels. Progress dragging keeps the real Windows cursor moving, tracks its pending desktop position locally, and sends a single mapped seek click to the main QQ Music window on mouse-up.
- A visual-only progress preview follows the pending seek position using the timeline captured at gesture start. It is not an input-owning controller overlay: it never activates or receives mouse input. Preview updates are coalesced on the UI thread and it disappears on release, cancellation, or wallpaper shutdown. A single in-memory backdrop sample is reconstructed around the old track and thumb to match the player skin; capture failure uses a transparent fallback. No screen sample is saved to disk.
- Accessibility caching uses observed source geometry and window state. Successful unchanged parking preserves the refresh deadline; source movement or recovery invalidates it. A snapshot captured across a geometry change is discarded instead of publishing old coordinates.
- A callback gap is a diagnostic observation, not proof that Windows removed the mouse hook. The watchdog excludes delayed polls that match the last delivered cursor position; optional diagnostics record callback timing and scan counts outside the hot input path.
- Lyric scrolling is routed to QQ Music. Exposed lyric timestamps are discovered while hovering and mapped onto the derived progress track for seeking.
- The Explorer theme runtime starts only when no external Windhawk instance is active and stops with the controller when the controller started it.
- Disabling or exiting the wallpaper restores ordinary access to QQ Music.
