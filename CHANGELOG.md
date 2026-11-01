# Changelog

## 1.2.0 - 2026-11-01

### 中文更新说明

#### 新增

- 支持在桌面单击或拖动进度条。轨道由 QQ 音乐公开的当前时长与总时长控件实时推导，拖动时真实光标保持可见且可移动，松手时只提交一次跳转。
- 支持点击歌词时间标签跳转到对应播放位置。
- 支持打开 QQ 音乐独立的原生评论窗口，并可在窗口内点击、滚动；点击桌面空白处会收起评论窗口，主播放器继续停留在桌面后方。
- 翻译、伴奏、皮肤、音质、音效、更多菜单、播放列表和评论入口统一通过 QQ 音乐公开的语义控件及其实时边界路由。
- 新增桌面输入策略回归测试，覆盖控件重叠、进度拖动、歌词跳转、弹窗归属、滚轮路由、关闭行为、重复控件树和缓存刷新。

#### 修复

- 修复播放键命中范围过大并覆盖进度条的问题；未发现语义控件时不再猜测固定屏幕位置。
- 修复进度拖动时光标冻结、音乐连续加速跳转、按下即跳转、松手后仍未到达目标等问题。
- 修复评论窗口弹到屏幕外、被主界面覆盖、触发任务栏提醒、无法滚动或点击空白处不能收起的问题。
- 修复评论、伴奏、音质、音效、皮肤、更多菜单和播放列表弹出缓慢或点击失效，以及滚动评论时误滚歌词的问题。
- 修复临时界面关闭逻辑向 QQ 音乐主表面发送 Escape，导致播放器返回首页的问题。
- 修复播放顺序入口与已展开菜单选项共用控件边界时选错目标的问题；随机、顺序、单曲循环和列表循环继续使用明确的原生模式值。
- 修复 QQ 音乐窗口恢复、最大化或移动后沿用旧控件映射的问题。
- 修复 QQ 音乐控件树同时保留两套底部播放栏时，旧播放栏抢占评论入口的问题。
- 降低主控件树重复扫描频率；原生弹窗打开期间直接复用其控件表面，减少交互迟钝。

### English

#### Added

- Click or drag the desktop timeline. Its bounds are derived from QQ Music's exposed current/total-time controls; the real cursor remains responsive and one seek is committed on release.
- Seek directly from exposed lyric timestamps.
- Open QQ Music's separate native comment window, interact and scroll inside it, then dismiss it with a blank desktop click while the main player remains behind the desktop.
- Route translation, accompaniment, player style, quality, effects, more-menu, playlist, and comment actions through exposed semantic controls and their live bounds.
- Add desktop-input regression coverage for overlap, progress dragging, lyric seeking, popup ownership, wheel routing, dismissal, duplicate accessibility trees, and cache refresh.

#### Fixed

- Removed the oversized play target that covered the timeline; missing semantic controls no longer fall back to guessed fixed screen positions.
- Fixed frozen cursor movement, repeated audible seeks, seek-on-press, and incorrect release targets during progress dragging.
- Fixed comment windows appearing offscreen, losing z-order, flashing the taskbar, rejecting wheel input, or remaining open after a blank click.
- Fixed slow or inert native surfaces for comments, accompaniment, quality, effects, player style, more-menu, and playlist, including wheel events leaking into lyrics.
- Prevented transient dismissal on QQ Music's main surface from navigating back to the home page.
- Fixed playback-order launcher selection when expanded menu choices share accessibility bounds; shuffle, sequential, single-repeat, and list-repeat retain explicit native values.
- Invalidated cached control mappings after source-window recovery, maximization, or movement.
- Selected the active player bar when QQ Music retains duplicate bottom bars in its accessibility tree.
- Reduced repeated main-tree scans and reused the active popup control surface to remove interaction lag.

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
