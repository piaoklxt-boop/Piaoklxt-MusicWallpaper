# QQ Music Wallpaper for Windows

[English](README.md)

把 QQ 音乐当前窗口变成可交互的 Windows 动态桌面。它保留 QQ 音乐播放页的动画效果，可直接在桌面点击原有播放控件，还能让 Windows 强调色随当前播放页或封面主色变化。

## 下载与使用

1. 从 [Releases](https://github.com/piaoklxt-boop/QQMusicWallpaper/releases) 下载 `QQMusicWallpaper-v1.0.0-win-x64.zip`。
2. 将压缩包完整解压到任意可写目录。
3. 双击 `QQMusicWallpaper.exe`。

不需要安装编译器、SDK、运行环境，也不需要手动配路径。程序不附带 QQ 音乐，请先安装官方 Windows 客户端。程序会从运行中的进程、卸载信息和常见目录自动查找 QQ 音乐；也可在托盘菜单中重新检测或手动选择 `QQMusic.exe`。

## 操作

- `Ctrl+Alt+U`：打开或关闭桌面壁纸。
- 托盘图标：切换壁纸、恢复 QQ 音乐、选择播放器路径、切换开机自启、设置主题色同步、检查更新或退出。
- 首次运行默认开启开机自启，可在托盘菜单关闭。

设置和日志存放在 `%LOCALAPPDATA%\QQMusicWallpaper`，不会污染解压目录，也能从只读目录运行。

## 兼容性

- Windows 11 x64：正式支持。
- Windows 10 1903 及以上：实验性支持。
- 需要另外安装 Windows 版 QQ 音乐。

壁纸镜像的是 QQ 音乐主窗口。评论窗口等独立弹窗可能只在原始 QQ 音乐窗口出现，不一定显示在桌面镜像中。

## 隐私与更新

程序不采集遥测数据。自动检查更新只会通过 HTTPS 访问本仓库的 GitHub Releases API；也可从托盘菜单手动检查。

## 从源码构建

需要 Visual Studio 2022 C++ 桌面生成工具、可构建 `net40` 的 .NET SDK 和 NuGet CLI。运行 `scripts\build-release.ps1`，便携目录和 ZIP 会生成到 `artifacts`。

## 许可证与商标

本项目使用 [MIT 许可证](LICENSE)。第三方代码说明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

这是独立社区项目，与腾讯没有隶属、授权或背书关系。QQ 音乐及相关名称和标志归其权利人所有；本项目不分发 QQ 音乐程序或腾讯素材。

