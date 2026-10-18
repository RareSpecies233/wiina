# Wiina 实施计划（待确认 · v0.1）

> 状态：**草稿，等待维护者确认**。确认前不开始写产品代码。
> 输入规格：[IINA-macOS-UI-复刻规格.md](reference/IINA-macOS-UI-复刻规格.md)（1230 行，基于 IINA `develop` @ `5c15f81`）。
> 许可证审计：[AUDIT.md](licensing/AUDIT.md)。视频管线决策：[ADR-0001](architecture/0001-video-pipeline.md)。

---

## 0. 一句话目标

在 Windows 上做一个 **IINA 的行为骨架 + Windows 11 Fluent 的皮肤 + mpv 的内核** 的桌面播放器：
一个主窗口、视频铺底、浮动 OSC、左右侧栏、OSD、全屏 / 迷你 / 画中画，以及设置 / 欢迎等二级窗口。

**非目标（首版）**：不做 macOS 的 Touch Bar、Force Touch、Services、SF Symbols、系统级 Quick Look；
不追求逐像素复制 AppKit，而是“层级、顺序、状态迁移、触发条件、避让规则”一致（规格 §0.1）。

---

## 1. 已核实的事实

| 项 | 结论 |
|---|---|
| 上游基线 | `iina/iina` `develop` @ `5c15f81568b1f61f85219302843018e3c43c8d3b`（2026-10-16），与规格一致 |
| IINA 许可证 | GNU GPL v3（仓库 `LICENSE` 已核对） |
| mpv | v0.41.0（2025-12-21）起支持 `--d3d11-output-mode=composition` 与只读属性 `display-swapchain` |
| libmpv | 默认构建为 GPLv2+；只有 `-Dgpl=false` 构建才是 LGPLv2.1+ |
| 视频呈现 | `display-swapchain`（int64）可直接绑定到 XAML `SwapChainPanel`；尺寸用 `--d3d11-composition-size=WxH` 控制 |
| 本机环境 | Windows 11 25H2 (26200)；VS 2026 Community；已安装 .NET SDK 10.0.402（用户级 `%USERPROFILE%\.dotnet`）；Windows App Runtime 1.4–2.5 已存在 |
| 构建验证 | 最小 WinUI 3 工程已成功构建（`Microsoft.WindowsAppSDK` 2.5.1 + `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2705，`net10.0-windows10.0.26100.0`，unpackaged，self-contained） |
| 参考实现 | `subdiox/nami`（GPL-3.0，WinUI 3 + libmpv）证明了 composition swapchain + SwapChainPanel 路线；`Richasy/mpv-winui`（MIT）提供了 OpenGL render API 备选；`henzfdev/HC-Player` 无许可证，**不参考其代码** |

---

## 2. 产品决策（需逐项确认）

### 2.1 名称与许可证

- **建议名称**：`Wiina`（工作名），副标题 “Windows-native, IINA-inspired mpv player”。
  “IINA” 是他人项目名/商标，必须在 README、关于页、发布页写明 **not affiliated / not endorsed**，且不使用 IINA 图标。
- **建议许可证**：**GPL-3.0-only**。
  - 与 IINA（GPLv3）一致；与 mpv（GPLv2+，“or later”可选 v3）兼容。
  - 如果使用任何 IINA 的文案/键位配置/规格内容，GPL-3.0-only 是最稳妥的合规选择。
  - 不采用 MIT/Apache：GPLv3 代码不能反向 relicensing，且 libmpv 的常见 Windows 构建是 GPL。

### 2.2 平台

- **主目标**：Windows 11 22H2+（Mica、圆角、Segoe Fluent Icons、WinAppSDK 2.x 完整体验）。
- **尽力支持**：Windows 10 2004+（无 Mica，退化为 Acrylic/纯色；Segoe MDL2 fallback）。
- 架构：x64 优先；ARM64 在 Stage 8 评估（libmpv 有 aarch64 构建）。

### 2.3 视觉语言

- **IINA 负责“结构与行为”，Fluent 负责“外观”**：Windows 11 的字体、图标、圆角、材质、焦点、动效曲线。
- 参考 Windows 上的 Apple Music：半透明侧栏、圆角内容卡片、克制的分隔线、大标题 + 小说明、深色优先。
- 不使用 macOS 红绿灯；使用 Windows 系统 caption buttons（右上角）。
- 深色为默认；浅色/跟随系统可选。

### 2.4 范围

- **v1（Stage 0–8）**：可日常使用的本地/URL 播放器 + IINA 级 UI（OSC、侧栏、OSD、缩略图、全屏、迷你、PiP、设置、欢迎、检查器、快捷键）。
- **v2（后续）**：插件（WebView2）、在线字幕（OpenSubtitles）、yt-dlp、10 段均衡器/滤镜、多窗口、HDR/杜比视界、安装器/winget。
- **明确不做**：Touch Bar、Force Touch、macOS Services、SF Symbols、iina-cli 兼容层、浏览器扩展。

### 2.5 mpv 分发

- **仓库内不提交任何 mpv 二进制**。
- `tools/fetch-libmpv.ps1`：下载固定版本的 `mpv-dev-x86_64`（shinchiro 构建），校验 SHA256，解压 `libmpv-2.dll` + 头文件到 `third_party/libmpv/`（gitignored）。
- 发布包：附带 `libmpv-2.dll` 的 `licenses/`（mpv 的 GPL/LGPL 文本）+ 源码获取链接；发布说明记录确切构建版本。
- 运行时检查 `libmpv-2.dll` 版本 ≥ 0.41；缺失时给出可操作的中文错误提示和下载入口。

---

## 3. 技术架构

### 3.1 解决方案结构

```
Wiina.sln
├─ src/
│  ├─ Wiina.Core/                 net10.0
│  │   ├─ Models/                 MediaItem, TrackInfo, Chapter, Playlist
│  │   ├─ Settings/               AppSettings + JSON schema/version/migration
│  │   ├─ State/                  OscVisibility, SidebarState, FullscreenState
│  │   ├─ Input/                  KeyChord, KeyMap, mouse/wheel gestures
│  │   ├─ Text/                   time formatting, OSD templates, i18n keys
│  │   └─ Services/               interfaces: IPlayer, ISettingsStore, IClock, IHistory
│  ├─ Wiina.Mpv/                  net10.0-windows10.0.26100.0（无 WinUI 依赖）
│  │   ├─ Interop/                libmpv P/Invoke（client.h / render.h 子集）
│  │   ├─ MpvClient/              handle, event loop, property observe, command
│  │   ├─ MpvPlayer/              state mapping, tracks, playlist, chapters
│  │   └─ Native/                 version check, DLL resolution
│  └─ Wiina.App/                  net10.0-windows10.0.26100.0（WinUI 3）
│      ├─ App.xaml / MainWindow
│      ├─ Views/                  MainPage, SettingsWindow, WelcomeWindow, ...
│      ├─ Controls/               Osc, Timeline, VolumeSlider, Osd, Sidebar, TitleBar
│      ├─ ViewModels/             MainViewModel, PlayerViewModel, SettingsViewModel
│      ├─ Interop/                SwapChainPanel host, WM_SIZING, min-max info, cursor
│      ├─ Services/               dialogs, file pickers, dispatcher, logging, theme
│      └─ Assets/                 own icons, fonts metadata, strings (zh-Hans/en)
└─ tests/
   ├─ Wiina.Core.Tests/           xUnit
   └─ Wiina.Mpv.Tests/            interop/unit; 无 libmpv 时跳过
```

**依赖方向**：`App → Mpv → Core`；`Core` 不引用 Windows/WinUI，便于单测与未来跨平台。

### 3.2 视频管线（核心风险，Stage 0 验证）

```
mpv (gpu-next / d3d11 / composition)
   │  创建 DXGI composition swapchain（无 HWND）
   ├─ 属性 display-swapchain (int64) ──► ISwapChainPanelNative::SetSwapChain
   │                                      ▲
   │                                      └─ XAML SwapChainPanel（视频层，最底）
   └─ d3d11-composition-size=WxH（物理像素，随窗口/DPI 更新）
XAML 内容（标题栏/OSC/侧栏/OSD/预览）绘制在 SwapChainPanel 之上
```

- mpv 启动参数（草案）：
  `--vo=gpu-next --gpu-context=d3d11 --d3d11-output-mode=composition`
  `--force-window=immediate --idle=yes --keep-open=yes --no-terminal`
  `--input-default-bindings=no --osc=no --osd-level=0 --no-input-cursor`
- 尺寸：`d3d11-composition-size` 用 `round(DIP × RasterizationScale)`；用“已承诺尺寸 + CompositeTransform”避免 resize 黑闪（参考 nami 的 committed/target 方案，自己实现并测试）。
- 交互式 resize：`WM_ENTERSIZEMOVE` 期间不请求 mpv 改尺寸，结束后一次性提交。
- DPI：监听 `XamlRoot.Changed`；`IDXGISwapChain2::SetMatrixTransform` 做 buffer px → DIP 映射。
- 生命周期：`Unloaded` 先 `SetSwapChain(panel, 0)`，再销毁 mpv；防止 XAML 呈现已释放的 swapchain。
- 宽高比：窗口 `WM_SIZING` 锁定；输出接近视频比例（<1%）时 `keepaspect=no` 消除 1px 黑边。
- 备选：若 composition 在目标 GPU/驱动上失败 → ANGLE/OpenGL render API + SwapChainPanel（Richasy MIT 参考），或 SW render + D3D11 上传；Stage 0 记录决策。

### 3.3 线程与事件模型

- mpv 在专用后台线程跑 `mpv_wait_event` 循环；`mpv_wakeup_callback` 唤醒。
- 所有事件通过 `DispatcherQueue.TryEnqueue` 回 UI 线程；属性观察（`mpv_observe_property`）：
  `time-pos, duration, pause, volume, mute, speed, track-list, chapter-list, playlist, playlist-pos, media-title, metadata, cache, demuxer-cache-time, eof-reached, idle-active, video-params, width, height, display-swapchain, d3d11-composition-size, keepaspect`。
- 命令：同步命令只允许在事件线程执行；UI 线程用 `mpv_command_async` 或投递到事件线程；复杂参数用 node API。
- 大列表（track-list/chapter-list/playlist）解析在后台，结果以不可变模型回 UI。
- 时间标签 10 Hz 更新；进度条用 60 fps 合成动画可选（Stage 8）。

### 3.4 显式状态机（放在 Core，可单测）

- `OscVisibilityState`: `Hidden / WillShow / Shown / WillHide`，事件 `PointerEntered / PointerMoved / PointerExited / TimerElapsed / DragStarted / DragEnded`。
- `SidebarState`: `Hidden / WillShow / Shown / WillHide` + `Side / Type / Tab`。
- `FullscreenState`: `Windowed / AnimatingToFullscreen / Fullscreen / AnimatingToWindowed`，失败回滚。
- `ClickArbiter`: 单击/双击消歧（`GetDoubleClickTime()`），双击取消单击定时器。
- 动画期间反向事件必须取消旧回调（规格 §20.3）。

### 3.5 设置、持久化与文件

- `%LOCALAPPDATA%\Wiina\`：
  - `settings.json`（版本化、原子写、迁移）
  - `history.json`（播放历史 + 断点续播）
  - `mpv\mpv.conf`、`mpv\input.conf`（高级用户）
  - `logs\wiina-*.log`、`mpv.log`
- 设置项默认值对齐规格 §2；未识别键保留（向前兼容）。
- 播放列表：`.m3u/.m3u8` 读写；内置播放列表序列化为 JSON。
- 文件关联：HKCU 注册，首次启动询问；支持“打开方式”。

### 3.6 本地化

- `.resw`（WinUI 3 标准）+ `ResourceLoader`；语言：`zh-Hans`、`en`，默认跟随系统。
- 键名沿用规格 §17.1（`menu.*`、`osc_toolbar.*`、`sidebar.*`、`settings.*`、`osd.*`、`mini_player.*`）。
- 文案自己撰写；OSD 用带占位符的整句，不拼接句子。
- 中文术语按规格 §17.2。

### 3.7 日志与崩溃

- `Microsoft.Extensions.Logging` + 轻量文件 sink；滚动 7 天。
- mpv 日志用 `mpv_request_log_messages("info")` 转发到同一日志。
- 全局异常：`AppDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException`、`Application.UnhandledException`；崩溃时写 `crash-*.txt` 并提示日志路径。

### 3.8 测试与 CI

- `Wiina.Core.Tests`：状态机、时间格式、键位解析、设置迁移、播放列表。
- `Wiina.Mpv.Tests`：节点解析、命令行构造；无 `libmpv-2.dll` 时 `Skip`。
- CI（GitHub Actions，windows-latest）：`dotnet build` + `dotnet test`；不跑 GUI。
- UI 验收：每阶段 `docs/acceptance/stage-N.md` + 截图/录屏 + 手动清单；不引入 WinAppDriver（首版）。
- 代码质量：`dotnet format --verify-no-changes`、`TreatWarningsAsErrors`（Stage 3 后开启）。

---

## 4. IINA → Windows 11 Fluent 适配规范

### 4.1 映射原则

| IINA / macOS | Wiina / Windows 11 |
|---|---|
| 自绘 titlebar + 红绿灯 | `ExtendsContentIntoTitleBar` + 系统 caption buttons；自绘内容区 |
| `NSVisualEffectView(.titlebar/.sidebar/.popover)` | Mica（窗口底）+ 应用内 Acrylic（OSC/侧栏/OSD）；不支持时 85% 不透明纯色 + 1px 边框 |
| SF Symbols | Segoe Fluent Icons（Win11）/ Segoe MDL2 Assets（Win10 fallback）+ 自绘 SVG |
| 12/16pt 圆角 | 8/12dp（Fluent）；OSC 8，卡片 8，OSD 12，缩略图 4 |
| 系统字体 13/11/20 | Segoe UI Variable Text/Display；13/11/20；时间用等宽数字（`Segoe UI Variable` + tabular 或 Cascadia Mono） |
| 0.25s 淡入淡出（默认 ease） | 0.25s，`cubic-bezier(0.33,0,0.67,1)`；尊重“减少动画” |
| Cmd / Option / Control | Ctrl / Alt / Ctrl（冲突项见附录 B） |
| 触控板横滑切标签 | 触控板横向滚动 / `ScrollViewer` 横滑；鼠标用标签点击 |
| Force Touch | 长按 / 双击（可配置） |
| 系统 PiP | 自绘置顶小窗（Stage 6）；后续评估 Windows 11 PiP |
| NSTouchBar | 不做；用 `SystemMediaTransportControls`（媒体键/传输控件） |

### 4.2 设计令牌（首版建议值）

| 令牌 | 值 | 说明 |
|---|---|---|
| 主窗口最小 | 285×120 | 规格 §2.4 |
| 无视频默认 | 640×360 | 音频文件默认窗口 |
| 标题栏高 | 40（Win11）；32 紧凑 | 系统 caption 32 + 内容 8 |
| 标题栏左右内边距 | 12 / 8 | |
| 浮动 OSC | 460×64（可压到 200） | 规格 460×67；Fluent 圆角 8 |
| OSC 按钮 | 24×24（触控 32 热区） | |
| 底部 OSC 内边距 | 16 左 / 12 右 / 12 上 / 8 下 | |
| 侧栏宽 | 360，300–500，≤窗口 80% | |
| 侧栏外边距/卡片间距/内边距 | 16 / 18 / 12（紧凑 14/16/10） | |
| 卡片圆角 | 8 | 1px `#22FFFFFF` 边框（深色） |
| 侧栏标签栏高 | 52（紧凑 48） | |
| OSD 圆角/内边距 | 12 / 16×8 | 规格 16/10 → Fluent 12 |
| OSD 字号 | 20（可设置 1–500） | 次要 ×0.5，clamp 11–25 |
| 时间预览圆角 | 8 | |
| 缩略图 | 宽 160，圆角 4，1px `#99FFFFFF` 边框 | |
| 设置窗口 | 900×640（建议；规格 600×480 太挤） | 左栏 220，内容最小 560 |
| 欢迎窗口 | 720×520（建议；规格 560×440） | |
| 插件窗口 | 600×400 | v2 |
| OSC 自动隐藏 | 2.5s | 可设置 |
| OSD 自动隐藏 | 1s / 0.5s 淡出 | |
| 侧栏动画 | 0.24s ease-in；减少动画时淡入 | |
| 迷你控件 | 0.2s | |
| 深色背景 | 视频窗口纯黑；二级窗口 Mica | |
| 强调色 | 跟随系统 | |

颜色（深色默认）：进度已播放 `#4DFFFFFF`、未播放 `#1AFFFFFF`、滑块 `#CCFFFFFF`、边框 `#22FFFFFF`；
浅色对应规格 §2.2 的黑色透明度版本。最终以资源字典 `ThemeDictionaries` 管理。

### 4.3 主窗口视图栈（从底到顶）

```xml
<Grid x:Name="Root">
  <SwapChainPanel x:Name="VideoHost" />                 <!-- 视频 -->
  <WebView2 x:Name="PluginOverlay" Visibility="Collapsed" /> <!-- v2 -->
  <Grid x:Name="PipOverlay" Visibility="Collapsed" />
  <Grid x:Name="SidebarLeading" />
  <Grid x:Name="SidebarTrailing" />
  <local:OscBottom x:Name="OscBottom" />
  <local:OscFloating x:Name="OscFloating" />
  <local:TitleBar x:Name="TitleBar" />                  <!-- 含 Top OSC -->
  <local:AdditionalInfo x:Name="AdditionalInfo" />
  <local:Osd x:Name="Osd" />
  <local:TimePreview x:Name="TimePreview" />
  <local:ThumbnailPeek x:Name="ThumbnailPeek" />
  <local:BufferIndicator x:Name="BufferIndicator" />
</Grid>
```

- 视频层独立（`SwapChainPanel`），不做透明叠层合成；UI 全部在其上。
- 侧栏显示/隐藏实时改视频区域左右约束（edge-to-edge 时视频仍铺满，侧栏浮在上面；docked 时视频让位）。
- 标题栏/底部 OSC 的左右约束跟随侧栏内边缘。

### 4.4 标题栏

- 自绘内容：文档图标 16×16 + 标题（13pt，中间截断）+ 右侧 `[去黑边] [pin]` + 系统最小化/最大化/关闭。
- 窗口化：标题可见；全屏：按 OSC 位置切换（顶部 OSC 时标题行换 OSC 行；否则隐藏）。
- `AppWindow.TitleBar` 按钮透明、前景色随主题；深色视频上强制浅色图标。
- 拖动区域：标题栏空白 + （可选）视频区域拖动窗口；视频区域拖动阈值 3px。
- 双击标题栏：跟随系统“双击标题栏最大化”设置。
- 右键标题/图标：路径面包屑菜单（规格 §4.5）。

### 4.5 OSC

- **浮动**（默认）：两行，上排音量 / 控制簇 / 工具栏，下排进度 + 时间；宽 460，圆角 8，Acrylic 深色；可拖动、中心吸附、位置持久化。
- **底部**：全宽，一行；贴底；docked 时视频让位。
- **顶部**：标题栏第二行，高 24；全屏时获得额外 8pt。
- 显隐状态机见 §3.4；鼠标在 OSC/标题栏内不隐藏；拖动中不隐藏；PiP/交互模式/实时文字不隐藏。
- 进度条：`Slider` 自定义模板（可访问性/键盘免费）+ 底层 `Canvas` 画章节线、缓冲段、A-B 旋钮；拖动时暂停并记录原状态，松开恢复；悬停显示时间预览/缩略图。
- 音量：静音按钮 + 70px 滑块；>100% 画 2px 缺口。
- 工具栏：settings / playlist / PiP / fullscreen / music / subtitle / screenshot / plugins（v2）/ live text（v2）；顺序可配置。
- 窄窗口优先级：先隐藏工具，再音量，最后压缩进度条。
- RTL：进度条/音量条/控制簇固定 LTR。

### 4.6 侧栏

- 左右槽位；同一类型只在一侧；再次点同标签关闭；点同类型不同标签原地切换；另一类型替换。
- 宽 360（300–500），内边缘 4px 拖拽；0.24s 滑入/滑出；减少动画时淡入。
- 标签栏：图标胶囊（选中态 1px 边框 + 渐变），关闭按钮；leading 侧留出 caption 区域。
- 卡片：`Border` 8 圆角、Acrylic/半透明、1px 边框；标题行 16 图标 + 12 semibold + 右侧重置。
- 四个快速设置页：布局 / 视频 / 音频 / 字幕；卡片顺序按规格 §7.5。
- 播放列表：`ListView`（虚拟化、多选、拖拽重排、右键菜单、双击播放、行内进度）；章节列表。
- 侧栏可见时，其区域内点击/滚动不触发视频单击动作；edge-to-edge 点视频关闭侧栏。

### 4.7 OSD 与预览

- `OsdMessage` 模型（Core）：`Normal / WithText / WithProgress / WithPosition / WithLeftToRightText`；模板字符串 + 参数。
- 结构：图标（0.8×字号）+ 主文本 20pt + 次要文本 10pt + 12pt 进度条 + 可选附件。
- 同一时刻一条；新消息覆盖并重置计时；1s 自动隐藏，0.5s 淡出；持久 OSD 抑制普通 OSD，ESC 关闭。
- 位置：右下（距边 8）；全屏时钟/电量左下；缓冲指示器居中。
- 时间预览：进度条上方 5pt；缩略图优先上方，放不下放下方；OSD/侧栏覆盖进度条时隐藏。
- 缩略图：后台生成（mpv screenshot-raw 或 libmpv 帧捕获），按秒取最近帧，内存 LRU；未就绪不显示。

### 4.8 设置 / 欢迎 / 二级窗口

- 设置：`NavigationView` 左栏 11 页 + 顶部搜索；右侧 `ScrollViewer` + 卡片行；搜索跳转 + 1s 高亮描边；分区小圆点。
- 欢迎：Mica 窗口、应用图标、版本、打开文件/URL、继续播放行、最近文件列表、拖放。
- 检查器、按键编辑、关于、日志、打开 URL：标准 Fluent 窗口/对话框。
- 菜单：`MenuBar`（Alt 唤出）+ 右键上下文菜单 + 快捷键；完整菜单树对齐规格 §14。

### 4.9 图标

- 首选 Segoe Fluent Icons 字形；Win10 用 Segoe MDL2 Assets 近似。
- 首批需要：play/pause/prev/next、volume 0–3/mute、fullscreen/restore、PiP、settings、playlist、subtitles、screenshot、pin、remove-black-bars、music、plugins、live-text、close/chevron、loop/shuffle、chapter。
- Stage 1 建一个“图标映射测试页”，逐个核对字形与 24px 下的清晰度；缺口用自绘 SVG（GPL-3.0）。
- 不使用 SF Symbols 的 SVG/字体（Apple 许可仅限 Apple 平台）。

### 4.10 输入映射（默认，详见附录 B）

- 鼠标：左单击隐藏 OSC（默认，可改为暂停）；左双击全屏；右单击暂停；中键无；拖动窗口（>3px）。
- 滚轮：垂直音量、水平跳转（可配置）；进度条/音量条上强制对应动作；自然滚动反向。
- 键盘：Space、←/→ ±5s、↑/↓ 音量、F11 全屏、Esc 退出全屏、Ctrl+O 打开、Ctrl+, 设置、Ctrl+S 截图、Ctrl+L A-B、`[`/`]` 速度、`.`/`,` 逐帧、Q 退出（文本框聚焦时禁用）。
- 媒体键：`SystemMediaTransportControls`（播放/暂停/停止/上一首/下一首）。
- 所有键位可在设置中查看/修改（v2 编辑器；v1 只读 + `input.conf` 高级覆盖）。

---

## 5. 功能范围矩阵

| 模块 | v1（Stage 0–8） | v2 | 不做 |
|---|---|---|---|
| 本地文件 / 文件夹 / 拖放 | ✅ | | |
| URL / 网络流 | ✅ 基础（mpv 原生） | yt-dlp 解析 | |
| 硬件解码 / HDR 色调映射 | ✅ hwdec；HDR 基础 | HDR 输出精调 | 杜比视界 |
| 轨道切换（视频/音频/字幕） | ✅ | | |
| 外挂字幕 / 延迟 / 位置 / 缩放 | ✅ | 在线字幕 | |
| 章节 / 播放列表 / 历史 / 续播 | ✅ | 多窗口 | |
| 截图 / A-B 循环 / 逐帧 | ✅ | | |
| OSC 三种位置 / 自动隐藏 / 拖动 | ✅ | | |
| 侧栏四页 + 播放列表/章节 | ✅ | 插件侧栏 | |
| OSD / 时间预览 / 缩略图 | ✅ | 持久 OSD 高级附件 | |
| 全屏 / 置顶 / 迷你 / PiP | ✅ 自绘 PiP | 系统 PiP | |
| 设置 11 页 / 搜索 | ✅ 11 页，插件/网络页先占位 | 全部行 | |
| 欢迎 / 最近文件 | ✅ | | |
| 检查器 / 按键查看 / 关于 / 日志 | ✅ | 按键编辑 | |
| 插件系统（WebView2） | | ✅ | |
| 在线字幕（OpenSubtitles） | | ✅ | |
| 均衡器 / 视频滤镜 UI | | ✅ | |
| 多窗口 / 多播放器实例 | | ✅ | |
| 安装器 / winget / 文件关联 | Stage 8 基础 | 完善 | |
| Touch Bar / Force Touch / SF Symbols | | | ❌ |
| macOS Services / AppleScript / iina-cli | | | ❌ |

---

## 6. 阶段计划（每阶段独立验收 + PR）

> 规模：S ≈ 半天–1 天，M ≈ 2–3 天，L ≈ 4–6 天（对 agent 的“轮次”估算，仅用于排序）。
> 每阶段结束产出 `docs/acceptance/stage-N.md`（清单 + 截图 + 已知问题），你验收通过后才提交 PR。

### Stage 0 — 基线与风险验证（S/M）

**目标**：把“WinUI 3 + libmpv composition swapchain + XAML 覆盖层”跑通，确定后续不会推倒重来。

交付：
- `Wiina.sln` + 三个项目骨架；`global.json`、`.editorconfig`、CI；`tools/fetch-libmpv.ps1`（固定版本 + SHA256）。
- 最小 App：unpackaged、深色、自定义标题栏、`SwapChainPanel`、libmpv P/Invoke、事件循环。
- 播放内置 `testsrc`（或本地样本）；XAML 红色覆盖块在视频上方可见；窗口 resize / DPI / 全屏 / 退出。
- `docs/acceptance/stage-0.md`：环境、命令、截图、GPU/驱动信息、已知限制。
- ADR-0001 更新为“已验证”。

验收：
1. 干净环境按 README 可构建、可运行。
2. 视频在 `SwapChainPanel` 中显示，XAML 覆盖层在视频上方（无 HWND airspace）。
3. resize / 125% / 150% DPI / 全屏不黑屏、不闪烁（允许一次已知的 resize 瞬时缩放，记录）。
4. 关闭窗口无残留 mpv 进程/线程。
5. 许可证审计通过；仓库无 mpv 二进制、无 IINA 资产。
6. CI 绿。

**若失败**：切换备选（ANGLE/OpenGL render API 或 SW→D3D11 上传），在 ADR 记录，Stage 1 顺延但不改 UI 计划。

### Stage 1 — 静态骨架（M）

交付：
- 主窗口层级（§4.3）；标题栏（文档图标/标题/pin/去黑边/系统按钮）；edge-to-edge 与 docked 两种布局；最小尺寸 285×120；宽高比锁定；窗口缩放命令（0.5x/1x/2x/Fit）。
- 空 OSC 占位（浮动/底部/顶部三种壳）；侧栏左右空壳；深色/浅色/跟随系统；Mica/Acrylic 降级。
- 图标映射测试页；资源字典（颜色/字号/间距/圆角）。
- 验收清单（规格 §19.2 第 1 条 + 布局/标题栏子集），截图 1280×720、640×360、285×120。

验收：视觉与层级正确；resize 无跳动；docked 切换视频让位；标题栏按钮状态正确；无布局溢出。

### Stage 2 — OSC（L）

交付：
- 三种位置完整布局；播放控制簇、时间、进度条、音量、工具栏、速度标签。
- 显隐状态机 + 2.5s 自动隐藏 + 鼠标进入/离开 + OSC 内不隐藏 + 拖动中不隐藏。
- 浮动 OSC 拖动、中心吸附（±5）、边界、位置持久化；窄窗口优先级压缩。
- 进度条：章节线、缓冲段、A-B 旋钮、拖动暂停/恢复、悬停时间预览；音量 >100% 缺口；tooltip。
- 减少动画/禁用动画适配；RTL 规则。

验收：规格 §19.2 第 2–6 条；逐项录屏/截图；状态机单测。

### Stage 3 — 播放内核接入（L）

交付（3A 核心 / 3B 输入，内部检查点）：
- 打开文件/文件夹/URL/拖放/命令行/最近；mpv 事件与属性映射；播放/暂停/停止/跳转/音量/静音/速度。
- 轨道（视频/音频/字幕）、外挂字幕、章节、播放列表、下一首/上一首、A-B、截图、逐帧。
- 鼠标/滚轮/键盘默认映射（附录 B）；媒体键；OSD 消息（基础）；错误提示；断点续播/历史。
- 媒体矩阵：H.264/HEVC/AV1/VP9、MKV/MP4/WebM、音频 FLAC/MP3/AAC、字幕 SRT/ASS。

验收：真实文件播放稳定；seek 不卡 UI；键盘表全部可用；无句柄/内存泄漏（连续开关 20 次）。

### Stage 4 — 侧边栏（L）

交付：
- 容器/动画/宽度拖拽/关闭/标签栏；左右槽位与替换规则。
- 快速设置 4 页（布局/视频/音频/字幕），卡片、滑块、下拉、开关、数值框；即时生效 + 持久化。
- 播放列表/章节：拖拽重排、多选、右键菜单、双击播放、行内进度、元数据开关。
- 轨道选择器；均衡器（v1 可先只读/占位，按确认）。

验收：规格 §19.2 第 7–9 条；侧栏不遮挡 OSC/标题栏；edge-to-edge 点视频关闭。

### Stage 5 — OSD 与预览（M/L）

交付：
- OSD 模型/模板/图标/进度条；1s 自动隐藏、0.5s 淡出；持久 OSD（附件、窗口增长、ESC）。
- 缓冲指示器、全屏时钟/电量、网络流提示。
- 时间预览 + 缩略图生成/缓存/避让；拖动时隐藏。
- OSD 与侧栏/预览互斥规则。

验收：规格 §19.2 第 10–11 条；消息目录抽样 15 条；缩略图 1080p/4K 性能可接受。

### Stage 6 — 模式（L）

交付：
- 全屏状态机（进入/退出/失败回滚/标题栏切换/菜单栏/置顶恢复）；多显示器。
- 置顶；迷你播放器/音乐模式（72+封面、播放列表 300、音量气泡 148、0.2s 控件互斥）；音频自动进入。
- 自绘 PiP 置顶小窗（swapchain 转移/恢复、暂停重绘）；全屏与 PiP 互斥。
- 媒体键与系统传输控件。

验收：规格 §19.2 第 12–13 条；全屏进出 20 次无布局错乱；PiP 进出不黑屏。

### Stage 7 — 二级窗口与菜单（L）

交付：
- 设置窗口 11 页 + 搜索 + 分区指示 + 高亮；行样式；不可用项置灰。
- 欢迎窗口 + 最近文件 + 继续播放 + 拖放；检查器；按键查看/编辑；关于；日志；打开 URL。
- 菜单树（File/Playback/Video/Audio/Subtitles/Window/Help + 动态子菜单）；启用规则；标题随状态切换。
- 首次启动体验（默认设置、文件关联询问）。

验收：规格 §19.2 第 14–16 条；设置搜索跳转正确；菜单项无死项。

### Stage 8 — 打磨与发布（M/L）

交付：
- 动画曲线统一、减少动态、RTL、高对比度、键盘导航、AutomationProperties、屏幕阅读器抽查。
- DPI 100/125/150/200、多显示器、休眠唤醒、HDR 基础、性能（启动/4K/内存）。
- i18n zh-Hans/en 完整；日志/崩溃；安装包（zip + 可选 Inno Setup/MSIX）与 GPL 合规包（LICENSE/NOTICE/mpv 源码链接）。
- 发布文档、已知问题、贡献指南。

验收：规格 §19.2 全部（标注 v2 项）；性能目标；许可证合规检查清单全绿。

---

## 7. 验收与 PR 流程

1. 我在 `stage/N-*` 分支完成实现 + 自测 + `docs/acceptance/stage-N.md`。
2. 给你发**阶段验收包**：变更摘要、运行方式、验收清单逐条结果、截图/录屏、已知问题、下一步。
3. 你验收：通过 / 修改 / 调整范围。必要时我们回到计划对齐。
4. 通过后：提交（Conventional Commits）→ 推到你的 GitHub → 开 PR 到 `main`（draft 直到你点合并）。
5. 合并后进入下一阶段；每个 PR 只含一个阶段，避免巨型 diff。
6. 每个 PR 模板：目的、范围、验收清单、截图、风险、许可证影响。

**提交粒度**：每个 PR 内按“功能/修复/文档/测试”拆分 commit；不混入无关格式化。

---

## 8. 风险与备选

| 风险 | 影响 | 缓解 |
|---|---|---|
| composition swapchain 在部分 GPU/驱动/远程桌面失败 | 视频不显示 | Stage 0 先验证；备选 ANGLE/OpenGL 或 SW 上传；启动时自检 + 明确错误 |
| WinUI 3 resize 时 swapchain 撕裂/黑闪 | 体验差 | committed-size + Transform；`WM_ENTERSIZEMOVE` 延迟；Stage 0 实测 |
| WinUI 3 缺少底层能力（光标隐藏、非客户区命中、最小尺寸） | 交互不完整 | CsWin32 + 窗口子类化；把 Win32 细节封装在 `Interop/` |
| HDR / 杜比视界 | 画面发灰/过曝 | v1 只做 mpv 默认色调映射；HDR 输出留 v2 |
| 范围过大 | 长期无法交付 | 阶段门禁；v1/v2 矩阵；每阶段可独立使用 |
| 许可证/商标 | 无法发布 | GPL-3.0-only；NOTICE；不使用 IINA 资产/SF Symbols；发布前审计 |
| mpv DLL 版本/架构不匹配 | 启动崩溃 | 版本检查；x64/ARM64 分目录；清晰错误与下载链接 |
| 编解码器专利 | 法律风险 | 不额外捆绑编解码器；使用 mpv 构建自带能力；README 声明 |

---

## 9. 需要你确认的开放问题

> 建议值已标 ★；可以直接回复“按建议”，或逐条改。

1. **名称与许可证**：`Wiina` + GPL-3.0-only + 免责声明？还是换一个与 IINA 距离更远的名字？
2. **v1 范围**：先做“核心可用”（Stage 0–8，不含插件/在线字幕/yt-dlp/均衡器），还是首版就追求规格全量？
3. **平台与视觉**：Win11 22H2+ 主目标 + Win10 尽力支持；IINA 结构 + Fluent 皮肤（Mica/Acrylic/Segoe/8dp）？还是更贴近 IINA 原版（12dp、类 SF 图标）？
4. **窗口尺寸**：设置 900×640、欢迎 720×520（更 Fluent），还是严格按规格 600×480 / 560×440？
5. **mpv 分发**：仓库只放 fetch 脚本，发布包再带 libmpv（GPL 合规）？还是要求用户自备 mpv？
6. **PR 工作流**：你建好空仓库后把 URL 给我，我加 remote 并（若 `gh` 已登录）直接开 PR？还是我只产出分支 + PR 描述，由你手动 push？

---

## 附录 A：规格章节 → 阶段

| 规格章节 | 阶段 |
|---|---|
| §1 界面总览 / §3 主窗口 / §4 标题栏 | 1 |
| §5 OSC / §9 时间预览 | 2（缩略图 5） |
| §6 播放控制与输入 | 3 |
| §7 侧边栏 | 4 |
| §8 OSD | 5 |
| §10 迷你播放器 / §11 全屏与 PiP | 6 |
| §12 设置 / §13 欢迎 / §14 菜单 / §15 插件 / §16 辅助窗口 | 7（插件 v2） |
| §17 文案 / §18 跨平台 / §19 验收 / §20 附录 | 8 与全程 |
| §2 设计令牌 | 1 起，持续 |

## 附录 B：默认键位（Windows 草案）

| 键 | 动作 | 备注 |
|---|---|---|
| Space / P | 播放/暂停 | |
| ← / → | -5s / +5s | |
| Shift+← / Shift+→ | -1s / +1s | 精确定位 |
| ↑ / ↓ | 音量 +5 / -5 | |
| Ctrl+↑ / Ctrl+↓ | 音量 +1 / -1 | |
| Ctrl+M | 静音 | |
| Alt+← / Alt+→ | 上一帧 / 下一帧 | |
| Ctrl+Shift+← / Ctrl+Shift+→ | 上一句/下一句字幕 | |
| Ctrl+O | 打开文件 | |
| Ctrl+Shift+O | 打开 URL | |
| Ctrl+, | 设置 | |
| Ctrl+S | 截图 | |
| Ctrl+L | A-B 循环 | |
| Ctrl+Shift+L | 循环模式 | |
| Ctrl+← / Ctrl+→ | 上一首/下一首 | |
| `[` / `]` | 速度 ×0.5 / ×2 | |
| Ctrl+Backspace | 速度复位 1x | |
| F11 / Esc | 进入/退出全屏 | 双击视频也可 |
| Ctrl+Shift+P | 画中画 | |
| Ctrl+Shift+M | 音乐模式 | |
| Ctrl+Shift+S | 快速设置侧栏 | |
| Ctrl+Shift+B | 播放列表侧栏 | |
| Ctrl+0/1/2 | 窗口 0.5x/1x/2x | |
| Ctrl+- / Ctrl+= | 缩小/放大窗口 | |
| Q / Ctrl+Q / Alt+F4 | 退出 | 文本框聚焦时 Q 不生效 |

> 与 macOS 的 Cmd/Option 映射：Cmd→Ctrl，Option→Alt；`⇧⌘`→Ctrl+Shift。冲突项在 Stage 3 用“键位冲突检测”提示。

## 附录 C：明确不做的 macOS 特性

- 红绿灯按钮、Touch Bar、Force Touch 多级按压。
- SF Symbols、IINA 的 `.xcassets` 图标/图片、IINA 应用图标。
- macOS Services、AppleScript、Quick Look、Dock 菜单。
- `iina-cli` 兼容层、Safari/Chrome 扩展、`iina://` URL scheme（可选做 `wiina://`）。
- 逐像素复刻 AppKit 控件外观；以 Fluent 规范为准。
