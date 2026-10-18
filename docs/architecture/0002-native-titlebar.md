# ADR-0002：WinUI 3 扩展标题栏 + 随 OSC 自动隐藏

- 状态：**Accepted**（2026-10-18，Stage 3 之后；替代上一版“Windows 原生标题栏”方案）
- 相关：Stage 1 自绘标题栏、Stage 2 自动隐藏、Stage 3 用户反馈

## 背景

Stage 1/2 的 WinUI 3 扩展标题栏（`ExtendsContentIntoTitleBar` + 自绘标题栏）保留了系统 caption
按钮。DWM 把系统按钮画在 XAML 之上，自动隐藏/全屏时自绘标题栏淡出了，按钮却仍然显示，
表现为“最小化/最大化/关闭始终显示”（灰底或浮在视频上）。这是平台限制，不是可接受的开发态。

维护者的要求逐步明确为：

1. 用 **WinUI 3 的原生扩展标题栏**（不是切回普通 Windows 标题栏）；
2. 标题栏在**非全屏**时也要随 chrome 一起消失；
3. 应用自己的按钮不要放在标题栏，统一进底部控制栏（OSC），接近 macOS IINA 的组织方式。

## 决策

- `Window.ExtendsContentIntoTitleBar = true` + `SetTitleBar(TitleBar)`，保留 WinUI 3 扩展标题栏。
- 自绘 `TitleBarView` 只保留文档图标/标题；`ButtonPanel`（pin/layout/OSC/sidebar/theme/icons/window）
  永久折叠。应用操作统一进入 OSC 工具栏最右侧的 `⋯` More 菜单。
- 标题栏显隐复用 OSC 的 chrome 状态机：
  - `FadeChrome` 把 `TitleBar` 与 OSC 一起做 0.25 s 透明度动画。
  - 为避免系统 caption 按钮在 XAML 之上“残留”，`SetTitleBarVisible(false)` 同时把
    `AppWindow.TitleBar.PreferredHeightOption` 设为 `Collapsed`；显示时恢复 `Standard`。
    `Collapsed` 会把系统标题栏/按钮高度收为 0，配合自绘标题栏 opacity=0，实现真正消失。
- 顶部 OSC 不再占用标题栏，而是视频区顶部的 overlay（`OscTop`）。
- 全屏时强制隐藏标题栏（`ToggleFullscreen` + `FadeChrome` 双重保护），退出全屏后按当前
  chrome 状态恢复。
- 标题栏/按钮颜色按主题设置；扩展标题栏下不再使用 `DwmSetWindowAttribute` 的
  “原生窗口标题栏”方案。

## 后果

- 非全屏时标题栏与系统按钮会在 2.5 s 后随 OSC 消失；鼠标进入/移动时一起恢复。
- 完全隐藏系统 caption 按钮依赖 `TitleBarHeightOption.Collapsed`，不再需要 WM_NCHITTEST 屏蔽。
- 应用按钮集中在 OSC More，标题栏只承担拖动/标题显示；窗口按钮（系统 caption）属于平台 chrome，
  在标题栏可见时出现、隐藏时消失。
- 失去“标题栏始终可见”的普通 Windows 观感；这是维护者明确选择的 IINA 风格。
- 如果将来 Windows App SDK 改变 `PreferredHeightOption` 行为，需要重新验证 caption 隐藏。
