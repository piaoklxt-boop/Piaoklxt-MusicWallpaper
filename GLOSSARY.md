# Piaoklxt-MusicWallpaper Glossary

This glossary names the project-specific surfaces and interactions shared by the controller, renderer, documentation, and tests.

## Language

**Desktop mirror**:
The live QQ Music image presented behind normal Windows desktop icons.
_Avoid_: Wallpaper window, copied player

**Source player**:
The QQ Music main window whose current page supplies the desktop mirror.
_Avoid_: Real window, hidden app

**Accessory window**:
A separate QQ Music top-level window, such as comments, that is not part of the source player image.
_Avoid_: Mirror panel, controller popup

**Native popup**:
A temporary QQ Music interaction surface owned by either the source player or an accessory window.
_Avoid_: Overlay

**Controller overlay**:
A project-owned window used when QQ Music does not expose a reliable native surface, currently volume and playback-order selection.
_Avoid_: Native popup

**Semantic control**:
A QQ Music accessibility node identified by its role and meaning, with bounds discovered from the current control tree.
_Avoid_: Button coordinate, hitbox constant

**Control-level routing**:
Delivering an action to a semantic control or native popup surface discovered at runtime.
_Avoid_: Coordinate automation, screen macro

**Transient UI**:
Any temporary native popup, accessory window, or controller overlay that should close without changing the source player's page.
_Avoid_: Main page

**Seek gesture**:
A desktop timeline press, optional drag, and release that commits exactly one playback-position change on release.
_Avoid_: Native drag
