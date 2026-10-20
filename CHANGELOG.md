# Changelog

## 1.1.0 - 2026-10-20

### 中文更新说明

#### 主要更新

- 项目、程序、设置目录和发布包统一更名为 Piaoklxt-MusicWallpaper。
- QQ 音乐画面可持续渲染到桌面，同时保持桌面图标、左右键、文件重命名、拖动和歌词滚动可用。
- 新增稳定的桌面音量与播放顺序弹窗；播放顺序使用 QQ 音乐明确的原生模式值，一次操作只发送一次命令。
- 集成精简的文件资源管理器着色运行时，便携版无需单独安装 Windhawk。
- 新增系统配色保存与恢复、QQ 音乐自动查找和启动、单一托盘入口、检查更新与作者信息。
- 提供自包含 Windows x64 压缩包，完整解压后即可运行。

#### 次要限制

- 点击桌面歌词时间标签暂时不能跳转，但歌词滚动正常。
- 评论面板属于 QQ 音乐独立原生界面，桌面入口暂不启用。
- Windows 10 支持仍处于实验阶段；内置资源管理器着色主要面向 Windows 11。

### English

#### Main improvements

- Rename the project and repository to Piaoklxt-MusicWallpaper.
- Keep QQ Music rendered and interactive on the desktop while its source window remains outside the visible workspace.
- Preserve normal desktop use: icons, filename editing, left/right clicks, pointer movement, drag operations, and lyric scrolling continue to work.
- Add stable desktop overlays for volume and playback order. Playback-order choices now use QQ Music's explicit native mode values instead of repeated simulated clicks.
- Synchronize the taskbar, supported application chrome, and File Explorer accent colors without requiring a separate Windhawk installation.
- Save the user's Windows colors when rendering starts and restore them when rendering stops or the application exits.
- Detect and open QQ Music automatically, while retaining manual re-detection and executable selection.
- Ship as a self-contained Windows x64 ZIP with startup control, update checks, one tray icon, and author information.

#### Secondary limitations

- Clicking a lyric timestamp on the rendered desktop does not seek playback; ordinary lyric scrolling still works.
- QQ Music's native comment panel is disabled on the wallpaper because it is a separate surface that cannot be mirrored reliably.
- Windows 10 support remains experimental, and the bundled Explorer coloring targets Windows 11.

## 1.0.0 - 2026-10-18

### 中文

- 首次实现将 QQ 音乐主窗口镜像为可交互的 Windows 桌面壁纸。
- 保持桌面图标可用，并支持系统强调色与资源管理器顶部配色同步。
- 增加 QQ 音乐自动/手动检测、开机自启、GitHub 更新检查和便携式 Windows x64 发布包。

### English

- Mirror the QQ Music main window as an interactive Windows desktop wallpaper.
- Keep desktop icons usable while the wallpaper is active.
- Synchronize Windows accent and Explorer chrome colors with the current QQ Music page.
- Add automatic and manual QQ Music discovery.
- Add startup control, automatic GitHub release checks, and a manual update command in the tray menu.
- Store settings and logs under the current user's local application data folder.
- Ship a self-contained Windows x64 portable ZIP with Chinese and English UI text.
