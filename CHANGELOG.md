# Changelog

## 1.2.6 - 本地验证中，尚未发布

### 修复

- 原生弹窗从停放源窗口移动到桌面后显式请求面板及子窗口重绘，修复窗口状态和坐标正确却没有实际画面的情况。保持不激活源播放器，以及关闭面板不重新显示的约束。
- 新增分层窗口重绘回归；离屏测试验证重绘请求，实际 QQ 音乐画面仍需单独验收。

## 1.2.5 - 本地验证中，尚未发布

### 修复

- 已接管的共享面板被另一个入口复用时，允许按本次启动器重新定位；仍然可见才移动，关闭的面板不会被重新显示。
- 首次呈现后在最多 1.5 秒的有界阶段内校正原生延迟布局导致的位置回跳；新点击、请求失效、窗口隐藏或时限到达立即停止，不持续限制用户移动窗口。
- 位置校正同步更新输入边界，并复用已经确认的面板身份，避免重新扫描控件树。

## 1.2.4 - 本地验证中，尚未发布

### 修复

- 修复无标题的伴唱面板已经打开但留在屏幕外的问题。仅对当前源窗口拥有的无标题原生面板进行有界后台识别，同时核对伴唱、声伴比例、变速和变调特征；“伴”和品质变为“伴唱”的入口共用这条识别路径。
- 原生启动器弹窗按本次命中的按钮位置呈现，优先放在按钮上方并限制在屏幕工作区内，避免沿用源窗口停放时的边角坐标。
- 已识别的无标题面板在维护和切换关闭时保留身份；隐藏后不重新呈现，也不误报等待超时。无关窗口、阴影、特征不完整或已取消的请求继续拒绝接管。

## 1.2.3 - 本地验证中，尚未发布

### 修复

- 修复中文歌曲不显示翻译时，隐藏的“翻译歌词”控件与可见“伴唱”控件共用位置，导致点击“伴”被识别成翻译；命中和调用均排除已被可见控件替代的隐藏项，保留外语歌翻译与源窗口停放兼容路径。
- 独立弹窗仅通过已接管的真实桌面输入边界参与路由，不再相对源播放器投影任意同进程窗口，避免空白点击误报通用控件失败。
- 通用控件诊断区分目标缺失与消息投递失败。

## 1.2.2 - 本地验证中，尚未发布

### 稳定性与诊断

- 将原生弹窗与评论的发现、呈现、跟踪和关闭收拢到统一模块，共享请求失效规则；评论只投递一次打开操作，等待期间不重复点击。
- 面板移动后更新输入边界；多个候选同样匹配或无法确定窗口身份时取消并记录原因，避免猜测窗口。
- 默认在本机内存保留最近 32 次路由交互的技术记录，托盘可手动导出；导出不包含窗口原文、歌曲、歌词、账号、路径或截图，也不拼接旧运行日志。
- 连续三次交互失败才显示托盘提示，提示间隔至少一分钟；孤立取消保持安静。托盘提供恢复桌面交互入口，不重启 QQ 音乐或改变播放状态。
- 当前验证范围为本机使用的 Windows 11、官方 QQ 音乐和显示配置；自动回归通过不代替实机交互验收。

### 修复

- 品质入口显示为“伴唱”时仍按其稳定控件身份投递；伴唱、品质、音效及播放器样式的命中与调用共享同一个播放栏控件，避免改名和重复控件树把点击送到其他入口。
- 弹窗发现跳过阴影窗口，允许恢复未接管的已有面板，并为首次加载保留最长五秒的有界等待；新点击、关闭、源窗口变化或退出渲染会取消过期请求。
- 维护桌面后的源窗口时保留原生弹窗的窗口层级，避免仍可见的所属弹窗被一并压到后台。

## 1.2.1 - 2026-11-01

### 修复

- 桌面进度条拖动时提供即时、鼠标穿透的视觉预览，仍在松手时只提交一次实际跳转。
- 拖动预览使用细轨道和小滑块，并从轨道周围重建当前皮肤背景，避免固定深色矩形遮挡与双滑块。
- 评论入口缓存未命中时进行有时限的后台语义核对，通用控件路径也会正确打开评论附件窗口；移动中产生的过期控件树不会发布。
- 评论入口的命中与调用统一选择当前播放栏，避免重复控件树仍把点击发往旧播放栏。
- 重复维护未移动的源窗口时保留控件缓存刷新计时；实际移动或恢复窗口时仍立即使缓存失效。
- 鼠标钩子已报告当前位置时，不再因维护轮询延迟而误判需要重装。其他回调缺口仍保留恢复机制；日志明确区分观测与未确认的超时原因。

### 验证与诊断

- 新增使用测试进程自有屏幕外窗口的控制器回归，验证真实消息投递和窗口维护逻辑；测试不操作用户的 QQ 音乐或桌面。
- 新增鼠标钩子监测回归，并在 CI 打包前执行全部回归和播放模式契约检查。
- 五个测试项目覆盖输入策略、评论语义解析、窗口维护、钩子监测及进度预览；预览测试使用合成深浅背景验证样式，不操作真实播放器。
- `--diagnostics` 可每 30 秒记录本地输入回调计数、耗时及控件扫描次数，不记录光标坐标，不上传数据。

### English

- Show a non-activating, pointer-transparent timeline preview while dragging, while still committing one actual seek on release.
- Blend the slim preview track and smaller thumb into the current player skin by reconstructing the surrounding background instead of drawing a fixed dark panel.
- Resolve an initially missed comment click through a bounded semantic probe and cancel stale requests; reject control snapshots captured across source movement.
- Resolve comment hit testing and invocation through the same active player-bar selector.
- Preserve the accessibility refresh deadline during unchanged source maintenance, while invalidating after real movement or recovery.
- Avoid reinstalling a healthy mouse hook when a delayed poll observes an already-delivered cursor position. Other callback gaps retain recovery and neutral diagnostic wording.
- Add controller message-dispatch and source-maintenance regressions using process-owned offscreen windows, plus watchdog regressions, and run all suites before CI packaging.
- Add optional local `--diagnostics` summaries without recording cursor coordinates or sending telemetry.

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
