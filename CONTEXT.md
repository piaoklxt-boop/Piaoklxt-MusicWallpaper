# Domain context

Canonical project terms are defined in [GLOSSARY.md](GLOSSARY.md). Architecture decisions are recorded under `docs/adr/`.

## Invariants

- QQ Music remains a separately installed application and is never distributed with this project.
- The desktop icons remain usable while the mirror is active.
- Wallpaper startup expands QQ Music's player/details page through its exposed accessible control and verifies the visible page state; screen coordinates are never fixed to one resolution.
- Explorer owns desktop icons and active filename editing; QQ Music owns blank rendered-player areas. Pointer clicks, drags, hover and lyrics-wheel input are mapped from the current desktop work area to the live QQ Music window and forwarded in the background, so desktop interaction never presents or focuses the source window.
- User settings and logs live outside the portable release folder.
- Desktop volume and playback-order pop-ups belong to the controller; their changes are applied to QQ Music in the background. The comment button clicks the exact exposed control bounds in the offscreen source and repositions the separate comment top-level window into the visible working area. A blank desktop click hides that window while the main source remains behind the desktop.
- Media buttons use their exposed accessible bounds when available. QQ Music does not expose the progress track as a slider, so the controller derives it from the current/total-time labels. Progress dragging keeps the real Windows cursor moving, tracks its pending desktop position locally, and sends a single mapped seek click to the main QQ Music window on mouse-up.
- Lyric scrolling is routed to QQ Music. Exposed lyric timestamps are discovered while hovering and mapped onto the derived progress track for seeking.
- The Explorer theme runtime starts only when no external Windhawk instance is active and stops with the controller when the controller started it.
- Disabling or exiting the wallpaper restores ordinary access to QQ Music.
