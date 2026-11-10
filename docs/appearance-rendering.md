# Office 与流动主题渲染

控制面板的 Microsoft Office 页分别控制 Word、Excel、PowerPoint。界面区域、内容区域和颜色强度独立保存。内容包含 Word 编辑区域、Excel 网格和 PowerPoint 幻灯片；显示效果不写入文档格式，打印／导出的原始颜色不变。

单色模式从已有 QQ 音乐语义快照中的播放／暂停键取色，不再从封面挑色。采样前后核对窗口身份、位置、尺寸和快照期限。无法取得可信采样时保留上一次有效色，新会话则等待首次有效采样。

暖色、冷色、全色炫彩同时保留多个颜色区域，以不同运动轨迹持续混合。动画在主题工作线程的显示层刷新，系统强调色使用稳定代表色，避免每帧广播系统设置。显示层为不激活、鼠标穿透的分层窗口，跟随原窗口层级、位置与最小化状态；关闭功能、暂停壁纸或退出后销毁。

## 区域识别

- Word、Excel 使用可见编辑控件的实时边界；未暴露这些控件的版本尝试 UI Automation。
- PowerPoint 优先通过已运行实例的只读对象模型取得活动普通／幻灯片视图的画布坐标，核对文档窗口身份并拒绝重名歧义。无法确认时再尝试 UI Automation。不会启动、激活或编辑 PowerPoint。
- 单独开启界面或内容时，未知区域不着色；同时开启两者时渲染整个编辑窗口。PowerPoint 特殊视图、放大至画布超出窗口、后台非活动文档或未注册运行对象的情况可能触发前一种保守退让。
- Word／Excel 的编辑区域可能包括纸张周围空白、网格表头等原生控件范围。Office 使用半透明颜色显示层，不宣称修改其原生材质或增加原生亚克力。
- 外观坐标使用局部线程 DPI 上下文，避免改变既有 QQ 音乐输入坐标体系。

## 验证

`ControllerRegression.Tests` 的 appearance 用例覆盖控制面板提交与保存、区域遮罩、真实测试窗口的归属／非激活样式／移动／最小化、暂停清理、多色空间差异与时间变化、播放键采样及旧坐标拒绝。其他四套回归和播放模式脚本继续按 AGENTS.md 执行。

可选 `--appearance-probe` 只读检查当前应用几何和渲染窗口；输出不含文档标题或正文。`--appearance-fixture <HWND>` 仅用于专门创建的空白 Office 测试文档，可检查实际区域开关并保存测试画面。此模式不会选择或编辑文档；不得把用户日常文档作为截图夹具。被遮挡的画面会跳过屏幕截图，单独标记原窗口截图，不能据此宣称显示层已通过视觉验证。

本机临时文档验证已覆盖 Word、Excel、PowerPoint 普通视图的区域切换和清理。Word 已观察到实际流动显示与内容区独立着色；其他被遮挡的实窗截图不计入视觉验收。合成颜色图和自动回归不能代替不同 Office 版本、缩放、多显示器、PowerPoint 特殊视图及实时 QQ 音乐采样的最终验收。

实现参考：[分层窗口及鼠标穿透](https://learn.microsoft.com/windows/win32/winmsg/window-features)、[Office UI Automation](https://learn.microsoft.com/en-us/office/uia/)、[PowerPoint 屏幕坐标转换](https://learn.microsoft.com/en-us/office/vba/api/powerpoint.documentwindow.pointstoscreenpixelsx)。
