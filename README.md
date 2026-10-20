# Piaoklxt-MusicWallpaper

[English](README.en.md)

[![构建便携版](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/actions/workflows/build.yml/badge.svg)](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/actions/workflows/build.yml)

把 QQ 音乐当前窗口变成可交互的 Windows 动态桌面。它保留 QQ 音乐播放页的动画效果，可直接在桌面点击原有播放控件，还能让 Windows 强调色随当前播放页或封面主色变化。

## 主要优势

- QQ 音乐画面实时铺在桌面图标后方，无需让 QQ 音乐窗口一直停在前台。
- 桌面播放、音量、播放顺序和歌词滚动均可直接操作。
- Windows 强调色、任务栏、受支持应用的窗口顶部及文件资源管理器可随播放页自动配色。
- 提供完整的 x64 便携包：解压即用，并可自动寻找或启动 QQ 音乐。
- 关闭桌面渲染或退出程序时自动恢复原系统配色。

## v1.1.0 版本更新说明

### 新增

- 增加桌面音量弹窗与播放顺序弹窗，避免依赖 QQ 音乐原窗口中的临时浮层。
- 集成精简的文件资源管理器着色运行时，便携版不再要求用户另外安装 Windhawk。
- 增加 QQ 音乐自动查找与启动、手动重新检测、托盘检查更新和“关于作者”。
- 增加进入渲染前保存系统配色、退出渲染后恢复原配色的完整流程。

### 修复与改进

- 修复播放顺序无法真正切换的问题；随机、顺序、单曲循环和列表循环现已使用明确的原生模式值。
- 修复多次加载转圈：一次选择只发送一次播放模式命令。
- 改进桌面图标、左右键、拖动、文件重命名以及歌词滚轮的输入归属判断。
- 改进全屏桌面尺寸、QQ 音乐源窗口隐藏和动态画面持续更新。
- 统一为单一托盘入口，并将程序、图标、设置目录和发布包更名为 `Piaoklxt-MusicWallpaper`。

### 当前已知问题

- 点击桌面歌词旁的时间标签仍不能跳转播放位置；失败的实验实现未放入正式版。
- QQ 音乐评论面板属于独立原生界面，暂时无法在桌面镜像中可靠使用。

完整历史请查看 [CHANGELOG.md](CHANGELOG.md)。

## 下载与使用

1. 从 [Releases](https://github.com/piaoklxt-boop/Piaoklxt-MusicWallpaper/releases) 下载 `Piaoklxt-MusicWallpaper-v1.1.0-win-x64.zip`。
2. 将压缩包完整解压到任意可写目录。
3. 双击 `Piaoklxt-MusicWallpaper.exe`。

不需要安装编译器、SDK、运行环境，也不需要手动配路径。程序不附带 QQ 音乐，请先安装官方 Windows 客户端。程序启动时会从运行中的进程、卸载信息和常见目录自动查找并打开 QQ 音乐；自动检测失败时，也可在托盘菜单中重新检测或手动选择 `QQMusic.exe`。

## 操作

- `Ctrl+Alt+U`：打开或关闭桌面壁纸。
- 托盘图标：切换壁纸、恢复 QQ 音乐、选择播放器路径、切换开机自启、设置主题色同步、检查更新、查看作者信息或退出。
- 桌面的音量和播放顺序使用稳定的壁纸弹窗，修改结果会在后台应用到 QQ 音乐。
- 首次运行默认开启开机自启，可在托盘菜单关闭。

设置和日志存放在 `%LOCALAPPDATA%\Piaoklxt-MusicWallpaper`，不会污染解压目录，也能从只读目录运行。首次启动会自动导入旧版 `QQMusicWallpaper` 目录中的设置。

## 兼容性

- Windows 11 x64：正式支持。
- Windows 10 1903 及以上：实验性支持。
- 需要另外安装 Windows 版 QQ 音乐。

壁纸镜像的是 QQ 音乐主窗口。目前点击桌面歌词旁的时间标签还不能跳转播放时间，但歌词滚动可正常使用。QQ 音乐的评论面板属于单独的原生界面，无法可靠镜像，因此桌面壁纸中的评论按钮已停用。

Windows 11 便携包内置精简的文件资源管理器着色运行时，可让资源管理器顶部跟随当前强调色，不需要另外安装 Windhawk。检测到已有 Windhawk 正在运行时，内置运行时会保持关闭，避免冲突。

进入桌面渲染时，程序会保存当前 Windows 配色；关闭桌面渲染或退出程序时会恢复原配色，再次进入时重新保存并开始新的配色会话。

## 隐私与更新

程序不采集遥测数据。自动检查更新只会通过 HTTPS 访问本仓库的 GitHub Releases API；也可从托盘菜单手动检查。

## 从源码构建

需要 Visual Studio 2022 C++ 桌面生成工具、可构建 `net40` 的 .NET SDK 和 NuGet CLI。运行 `scripts\build-release.ps1`，便携目录和 ZIP 会生成到 `artifacts`。

## 许可证与商标

Piaoklxt-MusicWallpaper 主程序使用 [MIT 许可证](LICENSE)。便携包还包含一个独立运行的 GPLv3 文件资源管理器着色组件，其源码和许可证随二进制文件一同提供。详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

这是独立社区项目，与腾讯没有隶属、授权或背书关系。QQ 音乐及相关名称和标志归其权利人所有；本项目不分发 QQ 音乐程序或腾讯素材。

## 关于作者

piaoklxt — <piaoklxt@gmail.com>
