# Domain context

## Terms

- **Desktop mirror**: The live QQ Music image drawn behind Windows desktop icons.
- **QQ Music main window**: The player, lyrics, or song page selected as the mirror source.
- **Accessory window**: A separate QQ Music pop-up, such as a comment window, that is not necessarily part of the mirrored main window.
- **Desktop interaction**: Mouse input performed on the mirror and delivered to the corresponding point in QQ Music.
- **Controller**: The tray application that finds QQ Music, manages lifecycle, input routing, startup, settings, and updates.
- **Renderer**: The native component that captures the QQ Music main window and presents it in the desktop layer.
- **Theme synchronization**: Updating the Windows accent and Explorer chrome colors from the current mirrored page.
- **Portable release**: A self-contained extracted folder that runs without a compiler, SDK, or installer.

## Invariants

- QQ Music remains a separately installed application and is never distributed with this project.
- The desktop icons remain usable while the mirror is active.
- User settings and logs live outside the portable release folder.
- Disabling or exiting the wallpaper restores ordinary access to QQ Music.
