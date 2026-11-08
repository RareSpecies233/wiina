# Wiina 原生 WinUI 3 覆盖检查报告（1.3.11 基线）

> 范围：`feat/v1.3.10` @ `11df58c` 工作树，只读审计。
> 方法：统计 33 个 XAML 文件的原生控件、逐文件检查 20 个自绘控件、5 个 Interop 文件、7 个 Service 与 `MainWindow` 分部类；对照 ADR-0001/0002/0003/0004 与 `docs/release/known-issues.md`。
> 结论不是“WinUI 3 好不好”，而是“哪些地方可以继续用原生、哪些必须魔改、哪些只是 WinUI 风味”。

---

## 0. 一句话结论

**WinUI 3 是 Wiina 的骨架，但不是全部。** 窗口、控件、主题、对话框、弹层都是原生的；**视频呈现、非客户区/标题栏、光标**三块必须绕开 WinUI 3 的公开能力；OSC/侧栏/时间轴/mini/PiP/设置行是“WinUI 风味”的自绘控件。真正需要警惕的不是自绘本身，而是 **自绘与原生非客户区/合成器不同帧** 带来的交互问题（本轮 1.3.11 的缩放慢一拍、侧栏半闪、吸附抖动都属于这一类）。

---

## 1. 原生 WinUI 3 覆盖（用到的部分）

### 1.1 窗口与呈现

| 能力 | 用法 | 文件 |
|---|---|---|
| `Window` / `AppWindow` / `OverlappedPresenter` / `FullScreen` | 主窗、PiP、设置窗、全屏 | `MainWindow.xaml.cs`、`MainWindow.Modes.cs`、`PipWindow.xaml.cs`、`Views/*` |
| `ExtendsContentIntoTitleBar` / `AppWindowTitleBar` | 扩展标题栏、自绘标题栏 | `MainWindow.xaml.cs:214`、`WindowShell.cs:45`、`PipWindow.xaml.cs:70` |
| `DesktopAcrylicBackdrop` / `MicaBackdrop` | 设置/欢迎/播放窗背景 | `MainWindow.xaml.cs:1900`、`WindowShell.cs:166` |
| `SwapChainPanel` | 视频承载（**原生控件，但绑定是 interop**） | `Controls/MpvVideoView.xaml`、`SwapChainPanelInterop.cs` |
| `ThemeShadow` / `CompositeTransform` / `RectangleGeometry` | 卡片阴影、DPI 变换、裁剪 | `ThumbnailPeek.xaml`、`MpvVideoView.xaml.cs`、`MainWindow.xaml.cs` |
| `ElementTheme` / `ThemeResource` / `UISettings` | 深浅/高对比/强调色 | `App.xaml.cs`、`Themes/Tokens.xaml`、`Services/WindowShell.cs` |

### 1.2 原生控件（33 个 XAML 文件统计）

| 控件 | 数量 | 典型位置 |
|---|---:|---|
| `Button` | 112 | OSC、侧栏、标题栏、设置、对话框 |
| `ComboBox` | 29 | 设置页、快速设置 |
| `ToggleSwitch` | 20 | 设置页、快速设置 |
| `Slider` | 15 | OSC 音量、mini 进度/音量、设置 |
| `NumberBox` | 13 | 设置页、画质编辑器 |
| `ListView` | 13 | 播放列表、章节、插件、快捷键 |
| `CheckBox` | 6 | 设置/滤镜 |
| `TextBox` | 5 | URL/搜索/数字输入 |
| `ProgressRing` | 2 | 缩略图加载、欢迎页 |
| `ProgressBar` | 1 | 播放列表行内进度 |
| `MenuFlyout` | 41 个 `new` | 应用菜单、右键菜单、轨道/速度子菜单 |
| `ContentDialog` | 9 个 `new` | 确认、警告、URL、卸载 |
| `NavigationView` / `TabView` / `Expander` / `InfoBar` / `TreeView` / `TeachingTip` | **0** | 未使用 |

> 说明：`MenuFlyout`/`ContentDialog` 主要在 C# 里动态创建（`MenuFlyout` 65 处引用、`ContentDialog` 24 处），不在 XAML 里声明；因此没有统一的 `MenuFlyoutPresenterStyle`，1.3.10 的菜单样式只能逐个创建点处理。

### 1.3 主题、字体、图标与无障碍

- `Themes/Tokens.xaml`：深/浅/高对比三套 `ChromeSolidBrush`、`OscBlurTintBrush`、`SidebarBlurTintBrush`、`PanelBrush`、`Slider*Brush` 等；**Fluent 令牌是自建的**，不是 WinUI 的 `ThemeResource` 全量映射。
- `Segoe UI Variable` / `Segoe MDL2 Assets`（`IconFont`）：原生字体 + Fluent 图标。
- 自动化名称：`loc:Loc.AutomationName`、`AutomationProperties`、`AccessibilityMetadata`（`Stage11ReleaseTests` 校验）。
- 高对比度：`Tokens.xaml` 的 HighContrast 分支、`SystemColorWindowColor`。

---

## 2. WinUI 3 尚未覆盖 / 必须自绘的部分

| # | WinUI 3 缺口 | 现象 | Wiina 的绕法 | 文件 | 风险 |
|---|---|---|---|---|---|
| G1 | `SwapChainPanel` 是独立合成层，XAML 亚克力/`CompositionBackdropBrush` 采样不到视频 | 标题栏/OSC/侧栏无法用原生亚克力做“视频模糊” | `ChromeBlur` 8→4Hz `screenshot-raw` + Win2D 高斯模糊 + `WriteableBitmap` | `Interop/ChromeBlur.cs`、ADR-0003 | GPU 回读成本、设备丢失、首帧错位（1.3.11 P0-2） |
| G2 | 组合交换链拒绝 `IDXGISwapChain2::SetMatrixTransform`（`DXGI_ERROR_INVALID_CALL`） | 高 DPI 下无法用矩阵缩放 | `SwapChainPanel` 按物理像素布局 + `1/dpi` XAML `CompositeTransform` | `SwapChainPanelInterop.cs` 头注释、`MpvVideoView.xaml.cs:66-78` | 混合 DPI 下几何/命中未完全验证 |
| G3 | WinUI 非客户区不能完全自定义，caption 按钮/拖拽区/边框由 DWM 管 | 想要 IINA 式无边框标题栏 + 自定义按钮 | `SetWindowSubclass`、`WM_NCCALCSIZE`/`WM_NCHITTEST`/`WM_GETMINMAXINFO`、DWM corner/border、`SetTitleBar`、caption drag region | `Interop/WindowInterop.cs` | 右侧自绘按钮比非客户区晚一帧（1.3.11 P1-2）、全屏拖拽/命中问题 |
| G4 | `SetCaptionDragEnabled`/caption region 与非客户区不同步 | 全屏/侧栏/标题栏点击被抢 | 每次布局/全屏重设 caption region、`IsHitTestVisible` 断言 | `WindowInterop.SetCaptionDragEnabled`、`MainWindow.xaml.cs` | 历史死点、焦点守卫复杂 |
| G5 | 光标由 WinUI/CoreMessaging 输入线程重设为箭头 | 自动隐藏光标几乎无效 | `CursorHider` 对 7 个输入模块做 **IAT hook**，重定向 `user32!SetCursor` | `Interop/CursorHider.cs` | 杀软误报、SDK 模块改名后失效 |
| G6 | Popup/Flyout 在 `SwapChainPanel` 上方时亚克力首帧 fallback | 菜单/侧栏首帧闪白/黑 | 1.3.10 侧栏 `Reapply`；菜单仍用系统 presenter | `MainWindow.Menu.cs`、`SidebarHost.xaml.cs` | 1.3.11 仍有半闪（P0-2） |
| G7 | `DesktopChildSiteBridge` 内容岛被 WinUI 放在客户区下方 1px | 全屏顶部 1px 亮/暗条 | `CoverClientArea` 用 `SetWindowPos` 拉伸内容岛 | `WindowInterop.CoverClientArea` | 非公开视觉树行为，SDK 升级可能变 |
| G8 | 焦点/键盘：指针按下会 focus 控件，焦点转移会取消 `Click` | 全屏侧栏页签/开关点不动；Space/Enter 误触 | `OnRootGotFocus` 守卫（Pointer 焦点不抢）、`OnRootPreviewKeyDown`、mini mouse-only | `MainWindow.Playback.cs` | 历史回归多次（1.3.7/1.3.8） |
| G9 | 无原生“防休眠/电源请求”封装 | 播放中熄屏/睡眠 | `PowerCreateRequest`/`SetThreadExecutionState` | `Interop/PowerKeepAwake.cs`（1.3.10） | 组策略可覆盖；需提权验证 |
| G10 | 无原生“窗口边缘吸附” | IINA 式磁吸 | 自定义 `EdgeSnapSession` + `WM_MOVING` + 拖拽泵 | `Core/State/EdgeSnap.cs`、`MainWindow.xaml.cs` | 边缘抖动（1.3.11 P1-1） |
| G11 | 无原生媒体键/系统媒体会话 | 键盘媒体键 | `WM_APPCOMMAND` + SMTC + 默认键位 | `WindowInterop`、`Services/SmtcService.cs` | 媒体键绑定曾误伤系统音量（1.3.10 已删） |
| G12 | 无原生“视频缩略图/缩略图气泡” | 时间轴悬停预览 | 第二个 headless mpv + `screenshot-raw` + 磁盘/内存缓存 | `Services/ThumbnailService.cs`、`MainWindow.Osd.cs` | 预生成期间黑卡（1.3.11 P0-3） |
| G13 | 无原生“窗口 PiP/迷你窗口” | IINA 式小窗 | 主窗模式 + `WS_POPUP` PiP + 自绘 mini | `MainWindow.Modes.cs`、`PipWindow.xaml.cs`、`MiniPlayerView.*` | PiP 8px DWM 边、mini 交互（1.3.11） |
| G14 | 混合 DPI/多显示器几何无统一 API | 拖动/悬停/吸附位置偏差 | 自行处理 `XamlRoot.RasterizationScale`、`DisplayArea` | `MainWindow`、`WindowInterop` | 125/150/200% 与多显示器未完全验证 |

---

## 3. “魔改”清单（必须保留的 Win32/COM/Win2D）

| 文件 | 魔改内容 | 为什么必须 | 风险/维护点 |
|---|---|---|---|
| `Interop/WindowInterop.cs` | `SetWindowSubclass`；`WM_NCCALCSIZE`/`WM_NCHITTEST`/`WM_GETMINMAXINFO`/`WM_SIZING`/`WM_MOVING`/`WM_EXITSIZEMOVE`/`WM_APPCOMMAND`/`WM_POWERBROADCAST`/`WM_DISPLAYCHANGE`；`DwmSetWindowAttribute`；`SetWindowPos` 内容岛；caption drag region；`ShowCursor`/`SetCursor` | WinUI 3 不暴露这些 | 非公开行为、SDK 升级、全屏/DPI/多显示器 |
| `Interop/SwapChainPanelInterop.cs` | `ISwapChainPanelNative::SetSwapChain` vtable 调用 | WinUI 3 没有公开的 `SwapChainPanel.SetSwapChain` | COM 接口版本 |
| `Interop/ChromeBlur.cs` | mpv `screenshot-raw` + Win2D `GaussianBlurEffect` + `WriteableBitmap` + `CanvasRenderTarget` | XAML 亚克力采样不到视频 | GPU 成本、设备丢失、首帧时序 |
| `Interop/CursorHider.cs` | 进程内 IAT patch（7 个模块的 `user32!SetCursor`） | 输入线程会覆盖 `SetCursor` | 杀软、模块改名、未来 Windows |
| `Controls/MpvVideoView.xaml.cs` | 物理像素布局 + `1/dpi` 变换 | `SetMatrixTransform` 被拒 | 混合 DPI |
| `Interop/PowerKeepAwake.cs` | `PowerCreateRequest`/`SetThreadExecutionState` | 无 WinUI API | 组策略 |
| `MainWindow.xaml.cs` | 自绘拖拽/吸附/缩放/标题栏；`AppWindow.Move`；caption region | 无原生磁吸/无边框 | 抖动/慢一拍/焦点 |
| `Controls/TitleBarView.xaml` | 自绘最小化/最大化/关闭按钮 + 自绘标题 | 设计需要 | 与非客户区不同帧（P1-2） |
| `Controls/Osc*`/`SidebarHost`/`TimelineControl`/`VolumeSlider`/`MiniPlayerView`/`OsdText`/`PlaybackStatsPanel` | 自绘控件/动画/命中 | 复刻 IINA 交互 | 焦点/命中/DPI/动画 |

---

## 4. “WinUI 风味”清单（自绘但遵循 Fluent）

| 区域 | 自绘内容 | 遵循的 Fluent 元素 |
|---|---|---|
| 标题栏 | `TitleBarView`：图标、标题、Pin、菜单按钮、自绘 caption 按钮 | 40px 高、Segoe UI Variable、Fluent 图标、强调色、hover/pressed 状态 |
| OSC | `OscFloatingView`/`OscBottomView`/`OscToolbar`：浮动/底部/顶部三形态、时间轴、音量、工具按钮 | 圆角、亚克力/纯色、Fluent 图标、自动隐藏、焦点视觉 |
| 侧栏 | `SidebarHost`/`QuickSettingsView`/`PlaylistView`/`ChaptersView`/`PluginsView`：单层 7 页导航、滑入/淡入 | 下划线页签、圆角卡片、`ToggleSwitch`/`ComboBox`/`NumberBox` 原生控件 |
| 时间轴 | `TimelineControl`：3px 轨道、buffer、played、章节标记、3×15 拖拽条 | 原生 `Slider` 隐藏在其上，保留键盘/触摸 |
| 音量 | `VolumeSlider`：自定义音量条 + 静音 | 原生 `Slider` 语义、滚轮/键盘 |
| mini | `MiniPlayerView`：封面、标题/艺术家、进度、控制行、音量气泡、播放列表 | 72px 控制区、148px 气泡、200ms 交叉淡入 |
| PiP | `PipWindow`：无边框 `WS_POPUP`、自绘 transport、占位 | 主题资源、圆角、Fluent 按钮 |
| OSD/统计 | `OsdText`、`PlaybackStatsPanel`、`BufferIndicator`、`FullscreenInfo`、`FrameDropNotice` | 半透明面板、圆角、主题色、非命中 |
| 设置 | `SettingsWindow`：自绘左栏、搜索、分组卡片、行控件；**不是 NavigationView/SettingsCard** | 11 页、下划线/rail、`ComboBox`/`NumberBox`/`ToggleSwitch`、搜索高亮 |
| 快捷键 | `KeyBindingsEditor`：自绘录制面板、冲突检测、导入导出 | 原生 `ListView`/`TextBox`/`Button`、Fluent 对话框 |

> 这些自绘控件是产品差异化的核心（OSC/侧栏/时间轴/mini），不建议为了“纯原生”而推倒重来；但需要按 WinUI 的布局/焦点/主题/DPI 规则持续校准。

---

## 5. 建议与优先级

| 优先级 | 建议 | 关联问题 |
|---|---|---|
| P0 | 把右侧 caption 按钮改为 **原生 `AppWindowTitleBar` 按钮**（保留自绘标题栏其余部分），解决缩放慢一拍与命中/焦点问题 | 1.3.11 P1-2 |
| P0 | 侧栏/所有 chrome 的模糊层在动画期间不要 `Apply`，动画结束再重放；几何用父容器坐标，避免烘入滑入位移 | 1.3.11 P0-2 |
| P1 | 吸附/拖拽统一为“单一移动源”，DWM 风格只在手势结束/稳定后切换；加 trace 与纯逻辑测试 | 1.3.11 P1-1 |
| P1 | 缩略图预生成期间不画黑卡；`ThumbnailPeek` 的 `ShowLoading/ShowEmpty` 语义拆开 | 1.3.11 P0-3 |
| P1 | mini 的拖拽只从“非交互表面”启动；检查左键；slider 用紧凑样式；音量气泡锚定按钮 | 1.3.11 P0-4/5/P1-5 |
| P2 | 把“原生 / 魔改 / 风味”边界写进 ADR/开发指南，新增 WinUI SDK 升级检查清单 | 本报告 |
| P2 | 混合 DPI/多显示器/高对比度纳入每次版本的固定验收矩阵 | G2/G14 |

---

## 6. 附录：审计数据

- XAML 文件：33；自绘控件：20（`Controls/*.xaml`）。
- Interop：5（`ChromeBlur`、`CursorHider`、`PowerKeepAwake`、`SwapChainPanelInterop`、`WindowInterop`）。
- Services：7（`AudioDeviceService`、`ChromeTint`、`FileAssociationService`、`KeyChordMapper`、`SmtcService`、`ThumbnailService`、`WindowShell`）。
- 原生控件计数见 §1.2；`MenuFlyout` 65 处引用（41 个 `new`）、`ContentDialog` 24 处引用（9 个 `new`）。
- 关键 ADR：`0001-video-pipeline.md`（SwapChainPanel + composition）、`0002-native-titlebar.md`（扩展标题栏）、`0003-acrylic.md`（视频模糊）、`0004-solid-chrome.md`（纯色档）。
