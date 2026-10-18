# ADR-0002：原生标题栏 + OSC 工具栏

- 状态：**Accepted**（2026-10-18，Stage 3 之后）
- 相关：Stage 1 自绘标题栏、Stage 2 自动隐藏、Stage 3 用户反馈

## 背景

Stage 1 用 `ExtendsContentIntoTitleBar` 做了一个自绘标题栏，右侧保留系统 caption 按钮。
在 Stage 2 的自动隐藏/全屏组合下，系统按钮由 DWM 绘制在 XAML 之上，无法随自绘标题栏一起淡出，
表现为“最小化/最大化/关闭始终显示”，并出现 caption 区域灰底。这是系统性限制，不是可接受的开发态。

维护者要求：

1. 标题栏使用 **Windows 原生标题栏**；
2. 应用自己的按钮不要放在标题栏上，统一放到 **底部控制栏（OSC）**，接近 macOS IINA 的组织方式。

## 决策

- `Window.ExtendsContentIntoTitleBar = false`，不再调用 `SetTitleBar`；由 Windows 绘制标题、
  最小化/最大化/关闭和窗口菜单。
- 自绘 `TitleBarView` 设为 `Collapsed`（保留代码作为后续“沉浸式自定义标题栏”的参考，不参与布局）。
- 应用功能按钮（布局、OSC 位置、侧栏、主题、置顶、图标映射、窗口尺寸、设置）收进 **OSC 工具栏
  最右侧的 `⋯`（更多）菜单**；播放/列表/设置/全屏仍在 OSC 工具栏上。
- 顶部 OSC 不再占用标题栏，而是作为视频区顶部的 overlay（`OscTop`，复用 `OscBottomView`）。
- 原生标题栏跟随主题：通过 `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` 请求深色标题栏。

## 后果

- 修复“caption 按钮常驻/灰底”；系统按钮行为与普通 Windows 窗口一致。
- 自动隐藏只作用于 OSC（以及顶部 overlay），不再尝试隐藏原生标题栏；这是平台预期。
- 失去“视频延伸到标题栏下方”的 edge-to-edge 标题栏效果；视频从原生标题栏下方开始。
  如果将来需要沉浸式标题栏，应重新评估 custom titlebar + DWM caption color，而不是继续在
  原生 caption 上叠加自绘按钮。
- 所有窗口级功能仍可通过 OSC More、快捷键和菜单访问，不依赖标题栏按钮。
