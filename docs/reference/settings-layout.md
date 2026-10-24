# Wiina 设置布局快照（1.2.6-dev）

> **用途**：下一版要重做设置菜单布局，本文件是当前实现的**完整快照**，供重排页面/分节时对照。
> 这是一份**静态快照**，不是运行时截图；所有数字与行内容都从源码统计得到，日期
> **2026-10-24**（Asia/Hong_Kong），分支 **`feat/v1.2.6`**（版本文件仍为 1.2.5，本快照按
> 开发中的 1.2.6-dev 标记）。
>
> **覆盖范围**：设置窗口的 12 个页面 / 122 行 / 33 个分节、行控件与保存管线、设置能打开的
> 全部窗口与对话框、主窗口快速设置侧栏（含重复入口）、以及重构建议。
>
> **不包含**：主窗口菜单、OSC 菜单、欢迎窗口（不由设置打开）、插件系统（未实现）。
>
> **如何重新生成**：以 `src/Wiina.Core/Settings/SettingsCatalog.cs` 的 `BuildRows()` 为准
> （`rows.Add(...)` 共 122 处），配合 `src/Wiina.Core/Localization/SettingsLocalization.cs`
> 的英文覆盖、`src/Wiina.Core/Settings/AppSettings*.cs` 的属性默认值、
> `src/Wiina.App/Views/SettingsWindow.xaml.cs` 的 `RunActionAsync` 分支，以及
> `src/Wiina.App/MainWindow.Sidebar.cs` 的 `OnQuickSettingChanged` 分支重新统计。
> 本文件的行号均指当前工作树。

## 0. 关键数字

| 项目 | 数值 | 来源 |
|---|---|---|
| 设置页面 | **12**（目录）/ 11（左栏可点，`KeyBindings` 被跳过） | `SettingsCatalog.cs:17-34`；`SettingsWindow.xaml.cs:108-116` |
| 设置行 | **122** | `SettingsCatalog.cs` `BuildRows()` 内 `rows.Add(` 计数 |
| 分节 | 33 个分节块（按名称去重 31 个：音频“音量与输出”、字幕“外观”各出现两次） | 各页 `Section` 序列 |
| 动作行（Button） | **29** | `SettingsRowKind.Action` 行 |
| 行控件种类 | 目录行用 8 种（Toggle/Choice/Number/Text/AudioDevice/Action/Info/Font）；另 2 种运行时面板（KeyBindings / PluginList） | `SettingsModels.cs` |
| 英文覆盖 | 118 / 122 行（4 行缺失） | `SettingsLocalization.cs` |
| 快速设置分支 | **50** 个 `QuickSettingKey`，其中 **38** 个直接写 `_settings.<属性>`（37 个能找到同名设置行） | `MainWindow.Sidebar.cs:491-739` |
| 设置窗口默认尺寸 | 760×600 DIP（最小 600×440） | `AppSettings.Shell.cs:10`；`SettingsWindow.xaml:9-11` |

> 注：`docs/` 里此前写的“119 行”是 1.2.4 的旧口径；之后新增了 `ui.osc-autohide-seconds`，
> 本快照按代码统计为 **122 行**。

## 1. 全局外壳

### 1.1 窗口与布局

- 设置窗口默认 760×600 DIP（`src/Wiina.Core/Settings/AppSettings.Shell.cs:10`），启动时把保存值
  限制在 600–2400 × 440–1800（`src/Wiina.App/Views/SettingsWindow.xaml.cs:67-71`）；XAML 根
  `RootGrid` 最小 600×440（`SettingsWindow.xaml:9-11`）。
- 外壳结构：48 DIP 标题行（`SettingsWindow.xaml:136`）→ 左栏固定 200 DIP + 右栏最小 400 DIP
  （`:185-186`）。标题栏可拖动；搜索框 280×32（`:160-163`），右侧是窗口按钮。
- **左栏是自定义导航**（不是 `NavigationView`）：`SettingsNavButtonStyle`（`:22-104`），图标 +
  文字，静止与悬停一致（不亮），选中项染色，键盘焦点为 2 px accent 环。
  `BuildNavigation()`（`.cs:108`）按 `SettingsCatalog.Pages` 顺序生成，并**跳过
  `KeyBindings`**（`:114-116`）；图标/文字显式绑定 Foreground（`:162`），避免主题回退色造成
  假高亮。
- 页面内容：页标题 + 页摘要来自 `SettingsPageInfo`；分节标题由 `AddSectionHeader()` 插入
  （`.cs:286`），滚动时吸附（`.cs:355` 的 `_sectionHeaders`）。
- 搜索：`SearchBox` 文本变化 → `SettingsSearchIndex` 命中列表 → 点击调用
  `Navigate(hit.Page.Id, hit.RowId)`（`.cs:989`），可深链到具体行。
- 深链入口：`App.ShowSettingsWindow(page, rowId)`（`src/Wiina.App/App.xaml.cs:449-470`）：
  已打开则 `NavigateTo(page, rowId)`（`SettingsWindow.xaml.cs:109`）并置前，否则新建窗口，
  构造时直接 `Navigate()`（`:183`）。菜单 / OSC 菜单 / 欢迎页用它跳到某个设置行。

### 1.2 行控件

| `SettingsRowKind` | 实际控件 | 说明 | 渲染位置 |
|---|---|---|---|
| `Toggle` | `ToggleSwitch` | 布尔值 | `SettingsWindow.xaml.cs:485` |
| `Choice` | `ComboBox` | 索引 0..n-1，写入前 `Math.Clamp` | `:512` |
| `Number` | `NumberBox` + 后缀 | min/max/step 由行定义 | `:544` |
| `Text` | `TextBox`（宽 220） | 字符串，失焦/回车保存 | `:593` |
| `AudioDevice` | `ComboBox`（音频设备） | libmpv `audio-device-list`；值=设备名，`auto`=跟随系统 | `:619` |
| `Action` | `Button` + 动词文案 | `ActionId` → `RunActionAsync` 分支 | `:693` |
| `Info` | 只读 `TextBlock`（宽 240） | 状态或运行值 | `:717` |
| `Font` | 字体按钮（宽 240） | 打开字体选择器，值=字体族 | `:733` |
| `KeyBindings` | 内嵌编辑器面板 | 目前只在独立窗口使用 | `:760` |
| `PluginList` | 插件列表占位 | Stage 9 未实现 | `:771` |

行布局：左侧标题 + 灰色描述（描述可为空），右侧控件；`Number` 宽 108（`.cs:553`）、
`Text` 220（`:598`）、`Info` 240（`:723`）、`Font` 240（`:773`）。

### 1.3 保存与应用管线

1. 行控件变更 → `Apply(row, value, immediate)`（`SettingsWindow.xaml.cs:838`）：
   `immediate: true` 的行（开关、下拉）立即 `SaveNow()`（`:862-864`）；其余（数字框、文本框）
   走 350 ms 防抖 `DispatcherQueueTimer`（计时器定义 `:79-82`，启动 `:869-870`）。
2. 计时器到点 → `SaveNow()` → `App.SaveSettings()`（`src/Wiina.App/App.xaml.cs:349-350`）：
   `SettingsStore.Save(Settings)` 落盘 `settings.json`，随后 `SettingsChanged?.Invoke()`。
3. 订阅者各自应用：设置窗口 `OnSettingsChanged`（`:86`、`:931`）重建行；主窗口把新值下发到
   mpv/OSC/侧栏/主题；`ChromeTint`/`Osd` 等直接读 `App.Settings`。
4. `Action` 行**立即执行**（`RunActionAsync`，`.cs:994`），不经过防抖；需要落盘的动作显式
   `SaveNow()`（如各 reset 分支）。

> 这意味着：重构行布局时只要保留 `SettingsRowDefinition` 的 getter/setter 与 id，保存管线
> 与搜索/深链都不用改；动作行只需保留 `ActionId`。

## 2. 页面总览（顺序 = 左栏顺序）

| # | PageId | 中文 | English | 摘要 | 图标 | 行数 | 分节 |
|---|---|---|---|---|---|---|---|
| 1 | `General` | 通用 | General | 启动、外观与历史记录 | U+E713 | 7 | 外观与启动 / 播放与历史 / 播放列表 |
| 2 | `Ui` | 界面 | Interface | 窗口布局、OSC、OSD 与缩略图 | U+E790 | 21 | 窗口与 OSC / OSD / 全屏状态栏 / 缩略图 |
| 3 | `Video` | 视频 | Video | 硬件解码、画质、比例与画面变换 | U+E714 | 27 | 解码 / HDR 与色彩 / 滤镜 / 去 Logo / 画面 / 重置 |
| 4 | `Audio` | 音频 | Audio | 音量上限、延迟与输出设备 | U+E767 | 11 | 音量与输出 / 延迟 / 音量与输出 / 均衡器 / 滤镜 / 重置 |
| 5 | `Subtitles` | 字幕 | Subtitles | 字体、颜色、位置与延迟 | U+E8D8 | 15 | 外观 / 时间与位置 / 外观 / ASS 字幕 / 智能加载 / 重置 |
| 6 | `Network` | 网络 | Network | 缓存、代理与请求头 | U+E774 | 6 | 缓存 / 网络 |
| 7 | `Control` | 控制 | Controls | 鼠标、滚轮与键盘入口 | U+E765 | 8 | 鼠标与滚轮 / 键盘 / 重置 |
| 8 | `KeyBindings` | 按键绑定 | Key Bindings | 录制、冲突检测与导入导出 | U+E765 | 1 | 快捷键 |
| 9 | `Advanced` | 高级 | Advanced | 诊断、文件夹与维护 | U+E7EF | 12 | 诊断 / 文件夹 / 维护 |
| 10 | `Integration` | 文件关联 | File Associations | 让 Windows 用 Wiina 打开视频与音频 | U+E71B | 6 | 文件关联 / 关联格式 / 维护 |
| 11 | `Plugin` | 插件 | Plugins | 尚未实现（Stage 9）：占位与文件夹入口 | U+EA86 | 4 | 插件 / 已安装插件 |
| 12 | `Utilities` | 工具 | Utilities | 字体、日志、检查器与关于 | U+E90F | 4 | 工具 |

> `KeyBindings` 在目录里是第 8 页，但左栏跳过它；实际从“控制 → 快捷键”动作行打开独立窗口，
> 见第 4 节。

## 3. 逐页明细（按源码顺序）

> 每行给出：id、当前控件、中文标题、描述、默认/范围/选项、English 覆盖，以及定义位置与
> 它写入的 `AppSettings` 属性。默认值来自 `AppSettings*.cs` 的初始值；“（C# 默认）”表示该
> 属性没有显式初始化。

### 页面：通用（`General`，7 行，图标 U+E713）

页面说明：启动、外观与历史记录

English：General — Startup, appearance, and history

#### 分节：外观与启动 / Appearance & Startup

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `general.theme` | ComboBox | 主题 | 深色、浅色或跟随系统；立即应用到所有播放窗口。 | 默认 ThemeMode.Dark；选项：跟随系统 / 深色 / 浅色 | Theme — Dark, light, or follow the system; applied to every playback window immediately. | SettingsCatalog.cs:64 → `AppSettings.Theme` |
| `general.animations` | ToggleSwitch | 界面动画 | 关闭后 OSC、侧栏与 OSD 直接切换，不做淡入淡出。 | 默认 true | Interface animations — When off, OSC, sidebars, and OSD switch instantly without fades. | SettingsCatalog.cs:71 → `AppSettings.AnimationsEnabled` |
| `general.language` | ComboBox | 界面语言 | 选择界面语言；更改立即应用并记住，重启后保持。 | 默认 "system"；选项：跟随系统 / 简体中文 / English | Interface language — Choose the UI language; changes apply immediately and are remembered. | SettingsCatalog.cs:77 → `AppSettings.Language` |
| `general.welcome` | ToggleSwitch | 启动时显示欢迎窗口 | 没有通过命令行/最近文件打开媒体时显示欢迎页。 | 默认 true | Show Welcome on startup — Show the welcome page when no media was opened from the command line or recent list. | SettingsCatalog.cs:84 → `AppSettings.ShowWelcomeWindow` |
#### 分节：播放与历史 / Playback & History

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `general.resume` | ToggleSwitch | 继续上次播放位置 | 打开本地文件时从播放历史断点续播。 | 默认 true | Resume last position — Resume local files from the saved playback history position. | SettingsCatalog.cs:90 → `AppSettings.ResumePlayback` |
| `general.restore-window` | ToggleSwitch | 恢复上次窗口位置 | 启动时还原窗口尺寸与位置；关闭后总是居中显示。 | 默认 true | Restore window position — Restore window size and position at startup; when off, always center the window. | SettingsCatalog.cs:96 → `AppSettings.RestoreWindowPosition` |
#### 分节：播放列表 / Playlist

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `general.auto-playlist` | ComboBox | 自动添加播放列表 | 打开单个视频时，自动把同目录的媒体加入播放列表；手动模式只播放选中的文件。 | 默认 AutoPlaylistMode.SameFolder；选项：同目录文件 / 仅手动添加 / 同目录及子文件夹 | Auto-add to playlist — When a single video opens, also queue other media from its folder; manual mode plays only the selected file. | SettingsCatalog.cs:102 → `AppSettings.AutoPlaylistMode` |

### 页面：界面（`Ui`，21 行，图标 U+E790）

页面说明：窗口布局、OSC、OSD 与缩略图

English：Interface — Window layout, OSC, OSD, and thumbnails

#### 分节：窗口与 OSC / Window & OSC

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `ui.layout` | ComboBox | 布局 | Edge-to-edge 让视频铺满；Docked 让标题栏和底部 OSC 占位。 | 默认 WindowLayoutMode.EdgeToEdge；选项：Edge-to-edge（覆盖视频） / Docked（视频让路） | Layout — Edge-to-edge fills the window with video; Docked reserves space for the titlebar and bottom OSC. | SettingsCatalog.cs:113 → `AppSettings.Layout` |
| `ui.always-on-top` | ToggleSwitch | 窗口置顶 | 播放窗口始终位于其他窗口之上。 | 默认 false（C# 默认） | Always on top — Keep the playback window above other windows. | SettingsCatalog.cs:122 → `AppSettings.AlwaysOnTop` |
| `ui.lock-aspect` | ToggleSwitch | 锁定窗口宽高比 | 拖动窗口边缘时保持当前视频比例。 | 默认 true | Lock window aspect ratio — Preserve the current video aspect while resizing window edges. | SettingsCatalog.cs:128 → `AppSettings.LockAspectRatio` |
| `general.edge-snap` | ToggleSwitch | 拖动时吸附屏幕边缘 | 拖动窗口时，靠近显示器工作区边缘会实时贴合；拖离时会自动脱离。 | 默认 true | Snap to screen edges — While dragging, snap the window to the nearest work-area edge and pull away automatically, macOS style. | SettingsCatalog.cs:134 → `AppSettings.EdgeSnapping` |
| `ui.sidebar-side` | ComboBox | 侧边栏位置 | 快速设置、播放列表和章节从窗口哪一侧滑入。 | 默认 SidebarSide.Trailing；选项：左侧 / 右侧 | Sidebar side — Which side quick settings, playlist, and chapters slide in from. | SettingsCatalog.cs:141 → `AppSettings.SidebarSide` |
| `ui.sidebar-width` | NumberBox | 侧边栏宽度 | 300–500 DIP；窗口过窄时会自动收起侧边栏。 | 默认 360；范围 300–500，步进 10 DIP | Sidebar width — 300–500 DIP; the sidebar auto-collapses when the window is too narrow. | SettingsCatalog.cs:150 → `AppSettings.SidebarWidth` |
| `ui.osc-position` | ComboBox | OSC 位置 | 浮动、贴底或顶部标题栏内的播放控制条。 | 默认 OscPosition.Floating；选项：浮动 / 底部 / 顶部 | OSC position — Playback controls floating, docked at the bottom, or in the top titlebar. | SettingsCatalog.cs:157 → `AppSettings.OscPosition` |
| `ui.osc-autohide` | ToggleSwitch | OSC 自动隐藏 | 鼠标静止超过下面的延时后淡出；Docked 布局下不隐藏。 | 默认 true | OSC auto-hide — Fade out after the idle delay configured below; stays visible in Docked layout. | SettingsCatalog.cs:164 → `AppSettings.OscAutoHide` |
| `ui.osc-autohide-seconds` | NumberBox | 自动隐藏延时 | 鼠标静止多少秒后隐藏 OSC 与标题栏；0.5–30 秒。 | 默认 2.5；范围 0.5–30，步进 0.5 秒 | Auto-hide delay — Idle seconds before the OSC and titlebar hide (0.5–30 s). | SettingsCatalog.cs:170 → `AppSettings.OscAutoHideSeconds` |
| `ui.blur-mode` | ComboBox | 毛玻璃模式 | 固定/动态/始终会抓取视频帧做高斯模糊；关闭后使用不透明纯色 + 重点色染色，性能最好且不透出画面。 | 默认 ChromeBlurMode.Dynamic；选项：固定（静态帧） / 动态（跟随视频） / 始终（持续计算） / 关闭毛玻璃（纯色） | Chrome blur mode — Fixed / Dynamic / Always capture the video frame for a Gaussian blur; Solid uses an opaque panel colour with an accent tint (cheapest, never see-through). | SettingsCatalog.cs:177 → `AppSettings.ChromeBlurMode` |
#### 分节：OSD / OSD

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `ui.osd-enabled` | ToggleSwitch | 显示 OSD 消息 | 音量、跳转、轨道切换等状态提示。 | 默认 true | Show OSD messages — Status hints for volume, seeking, track changes, and more. | SettingsCatalog.cs:184 → `AppSettings.OsdEnabled` |
| `ui.osd-position` | ComboBox | OSD 位置 | 六个位置：左右下角、左右上角、顶部居中或底部居中。 | 默认 OsdPosition.BottomRight；选项：右下 / 左下 / 右上 / 左上 / 顶部居中 / 底部居中 | OSD position — Six placements: bottom/top left or right, plus top or bottom center; the fullscreen clock stays bottom left. | SettingsCatalog.cs:190 → `AppSettings.OsdPosition` |
| `ui.osd-size` | NumberBox | OSD 字号 | 按百分比缩放主文本；1–500%。 | 默认 100；范围 1–500，步进 5 % | OSD font size — Scale the primary text as a percentage; 1–500%. | SettingsCatalog.cs:197 → `AppSettings.OsdSizePercent` |
| `ui.osd-duration` | NumberBox | OSD 停留时间 | 自动隐藏前的停留秒数；隐藏动画固定 0.5 秒。 | 默认 1；范围 0.2–5，步进 0.1 秒 | OSD hold time — Seconds before auto-hide; the hide animation stays at 0.5 seconds. | SettingsCatalog.cs:204 → `AppSettings.OsdDurationSeconds` |
#### 分节：全屏状态栏 / Fullscreen Status Bar

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `ui.fullscreen-clock` | ToggleSwitch | 全屏时钟 | 全屏时在左下角显示时间。 | 默认 true | Fullscreen clock — Show the time in the bottom-left corner in fullscreen. | SettingsCatalog.cs:211 → `AppSettings.FullscreenClockEnabled` |
| `ui.fullscreen-battery` | ToggleSwitch | 全屏电量 | 全屏时在左下角显示电池与充电状态。 | 默认 true | Fullscreen battery — Show battery and charging state in the bottom-left corner in fullscreen. | SettingsCatalog.cs:217 → `AppSettings.FullscreenBatteryEnabled` |
| `ui.fullscreen-title` | ToggleSwitch | 全屏显示标题 | 在左下角状态行中包含当前媒体标题。 | 默认 false（C# 默认） | Show title in fullscreen — Include the current media title in the bottom-left status line. | SettingsCatalog.cs:223 → `AppSettings.FullscreenTitleEnabled` |
#### 分节：缩略图 / Thumbnails

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `ui.thumbnail-preview` | ToggleSwitch | 进度条缩略图 | 悬停进度条时显示时间预览与视频帧。 | 默认 true | Timeline thumbnails — Show a time preview and video frame when hovering the progress bar. | SettingsCatalog.cs:229 → `AppSettings.ThumbnailPreviewEnabled` |
| `ui.thumbnail-width` | NumberBox | 缩略图宽度 | 80–320 DIP；高度按视频比例计算。 | 默认 160；范围 80–320，步进 10 DIP | Thumbnail width — 80–320 DIP; height follows the video aspect. | SettingsCatalog.cs:235 → `AppSettings.ThumbnailWidth` |
| `ui.thumbnail-bucket` | NumberBox | 缩略图间隔 | 按秒分桶缓存，越大越省内存、精度越低。 | 默认 5；范围 1–30，步进 1 秒 | Thumbnail interval — Cache buckets per second; larger saves memory but is less precise. | SettingsCatalog.cs:242 → `AppSettings.ThumbnailBucketSeconds` |
| `ui.thumbnail-cache` | NumberBox | 缩略图缓存数量 | 内存中保留的缩略图张数。 | 默认 60；范围 10–240，步进 10 张 | Thumbnail cache size — Number of thumbnails kept in memory. | SettingsCatalog.cs:249 → `AppSettings.ThumbnailCacheCount` |

### 页面：视频（`Video`，27 行，图标 U+E714）

页面说明：硬件解码、画质、比例与画面变换

English：Video — Hardware decoding, picture quality, aspect, crop, and transforms

#### 分节：解码 / Decoding

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.hwdec` | ToggleSwitch | 硬件解码 | 使用 auto-safe；显卡/驱动不支持时自动回退软解。 | 默认 true | Hardware decoding — Use auto-safe; falls back to software decoding when unsupported. | SettingsCatalog.cs:258 → `AppSettings.VideoHardwareDecode` |
| `video.hwdec-mode` | ComboBox | 硬件解码模式 | 开启硬件解码时使用的 mpv hwdec 模式；auto-safe 最稳妥。 | 默认 "auto-safe"；选项：auto-safe / auto / d3d11va / dxva2 / no | Hardware decoding mode — mpv hwdec mode used when hardware decoding is on; auto-safe is safest. | SettingsCatalog.cs:264 → `AppSettings.VideoHwdecMode` |
| `video.mpv-stats` | ToggleSwitch | 播放统计面板 | 在画面右上角显示解码、渲染、音画同步与丢帧等完整统计。 | 默认 false（C# 默认） | mpv statistics overlay — Show raw mpv frame, render, and playback statistics on screen; useful for diagnosing dropped frames. | SettingsCatalog.cs:274 → `AppSettings.ShowMpvStats` |
| `video.frame-drop-osd` | ToggleSwitch | 丢帧提示 | 丢帧计数增加时在 OSD 中提示。 | 默认 true | Frame-drop OSD — Show a short OSD message whenever the dropped-frame counter increases during playback. | SettingsCatalog.cs:280 → `AppSettings.FrameDropOsd` |
| `video.deinterlace` | ToggleSwitch | 去隔行 | 对隔行扫描素材启用 mpv 去隔行。 | 默认 false（C# 默认） | Deinterlace — Enable mpv deinterlacing for interlaced sources. | SettingsCatalog.cs:286 → `AppSettings.VideoDeinterlace` |
#### 分节：HDR 与色彩 / HDR & Color

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.tone-mapping` | ComboBox | HDR 色调映射 | SDR 屏幕上显示 HDR 内容时使用的 mpv tone-mapping 算法。 | 默认 "auto"；选项：自动 / BT.2390 / ST2094-40 / ST2094-10 / Spline / Hable / Clip / Gamma / Reinhard / Mobius | HDR tone mapping — mpv tone-mapping algorithm used to show HDR content on SDR screens. | SettingsCatalog.cs:292 → `AppSettings.VideoToneMapping` |
| `video.hdr-compute-peak` | ComboBox | HDR 峰值计算 | 自动让 libmpv 根据内容计算峰值亮度；关闭可省一点性能。 | 默认 "auto"；选项：自动 / 开启 / 关闭 | HDR peak detection — Let libmpv compute peak brightness from content; off saves some performance. | SettingsCatalog.cs:312 → `AppSettings.VideoHdrComputePeak` |
| `video.target-peak` | NumberBox | 目标峰值亮度 | 色调映射的目标亮度（nits）；0 表示自动。 | 默认 0（C# 默认）；范围 0–4000，步进 50 nit（0=自动） | Target peak brightness — Target brightness for tone mapping (nits); 0 means automatic. | SettingsCatalog.cs:329 → `AppSettings.VideoTargetPeak` |
| `video.output-levels` | ComboBox | 输出范围 | auto/limited/full；全范围灰阶发灰时可尝试 full。 | 默认 "auto"；选项：自动 / Limited（16–235） / Full（0–255） | Output range — auto/limited/full; try full when full-range grays look washed out. | SettingsCatalog.cs:337 → `AppSettings.VideoOutputLevels` |
#### 分节：滤镜 / Filters

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.filters` | Button | 打开视频滤镜… | 添加/删除/排序/编辑滤镜，保存命名预设并即时应用到 mpv vf。 | 动作 `open-video-filters`（SettingsWindow.xaml.cs:1085） | Open video filters… — Add, remove, reorder, and edit filters, save named presets, and apply them to mpv vf immediately. | SettingsCatalog.cs:346 |
#### 分节：去 Logo / Remove Logo

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.delogo-enabled` | ToggleSwitch | 启用去 Logo | 用周围像素覆盖固定位置的台标；与滤镜列表里的 delogo 互斥。 | 默认 false（C# 默认） | Enable delogo — Cover a fixed logo position with surrounding pixels; mutually exclusive with delogo in the filter list. | SettingsCatalog.cs:351 → `AppSettings.VideoDelogoEnabled` |
| `video.delogo-x` | NumberBox | 去 Logo X | 矩形左上角 X（像素）。 | 默认 0（C# 默认）；范围 0–16384，步进 1 px | Delogo X — X coordinate of the rectangle's top-left corner (pixels). | SettingsCatalog.cs:357 → `AppSettings.VideoDelogoX` |
| `video.delogo-y` | NumberBox | 去 Logo Y | 矩形左上角 Y（像素）。 | 默认 0（C# 默认）；范围 0–16384，步进 1 px | Delogo Y — Y coordinate of the rectangle's top-left corner (pixels). | SettingsCatalog.cs:365 → `AppSettings.VideoDelogoY` |
| `video.delogo-width` | NumberBox | 去 Logo 宽度 | 矩形宽度（像素，至少 2）。 | 默认 320；范围 2–16384，步进 2 px | Delogo width — Rectangle width (pixels, at least 2). | SettingsCatalog.cs:373 → `AppSettings.VideoDelogoWidth` |
| `video.delogo-height` | NumberBox | 去 Logo 高度 | 矩形高度（像素，至少 2）。 | 默认 80；范围 2–16384，步进 2 px | Delogo height — Rectangle height (pixels, at least 2). | SettingsCatalog.cs:381 → `AppSettings.VideoDelogoHeight` |
#### 分节：画面 / Picture

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.quality` | ComboBox | 视频画质预设 | 两个内置档位：默认对齐 mpv/IINA 原生默认（Lanczos 放大、Hermite 缩小、Bilinear 色度）；性能优先为全 Bilinear 的省 GPU 档。23 项细调与导入/导出在“管理画质预设…”里。 | 默认 VideoQualityPreset.Auto；选项：默认 / 性能优先 | Video quality preset — Two built-in profiles: Default matches mpv/IINA defaults (Lanczos upscaling, Hermite downscaling, bilinear chroma); Performance first uses bilinear everywhere for weak GPUs. 23 fine-grained options and import/export live under Manage quality presets…. | SettingsCatalog.cs:389 → `AppSettings.VideoQualityPreset` |
| `video.quality-editor` | Button | 管理画质预设… | 创建、重命名、导入/导出 23 项画质选项（缩放/色度/抖动/去色带参数/插帧/同步/其他）；去色带总开关在下面单独控制。 | 动作 `open-video-quality`（SettingsWindow.xaml.cs:1089） | Manage quality presets… — Create, rename, and import/export 23 quality options (scaling, chroma, dithering, deband parameters, interpolation, sync, other); the deband master toggle is separate below. | SettingsCatalog.cs:406 |
| `video.deband` | ToggleSwitch | 去色带 | 默认关闭；开启后减轻渐变画面的色带，但会轻微柔化细节并增加少量 GPU 开销。 | 默认 false（C# 默认） | Deband — Off by default; reduces colour banding in gradients at the cost of slightly softer detail and a little extra GPU work. | SettingsCatalog.cs:411 → `AppSettings.DebandEnabled` |
| `video.aspect` | ComboBox | 宽高比 | 覆盖视频显示比例；默认跟随容器。 | 默认 "no"；选项： | Aspect ratio — Override the displayed video aspect; by default it follows the container. | SettingsCatalog.cs:417 → `AppSettings.VideoAspect` |
| `video.crop` | ComboBox | 裁剪 | 居中裁剪到指定比例；下次加载视频时重新计算。 | 默认 0（C# 默认）；选项： | Crop — Center-crop to the selected ratio; recalculated the next time a video loads. | SettingsCatalog.cs:432 → `AppSettings.VideoCropAspect` |
| `video.rotation` | ComboBox | 旋转 | 顺时针旋转 90° 的整数倍。 | 默认 0（C# 默认）；选项：0° / 90° / 180° / 270° | Rotation — Rotate clockwise in 90° increments. | SettingsCatalog.cs:446 → `AppSettings.VideoRotation` |
| `video.flip-h` | ToggleSwitch | 水平翻转 | 左右镜像画面。 | 默认 false（C# 默认） | Flip horizontally — Mirror the picture left to right. | SettingsCatalog.cs:453 → `AppSettings.VideoFlipHorizontal` |
| `video.flip-v` | ToggleSwitch | 垂直翻转 | 上下镜像画面。 | 默认 false（C# 默认） | Flip vertically — Mirror the picture top to bottom. | SettingsCatalog.cs:459 → `AppSettings.VideoFlipVertical` |
| `video.zoom` | NumberBox | 缩放 | −1（缩小）到 1（放大）；0 为原始大小。 | 默认 0（C# 默认）；范围 –，步进  | Zoom — −1 (zoom out) to 1 (zoom in); 0 is original size. | SettingsCatalog.cs:465 → `AppSettings.VideoZoom` |
| `video.pan-x` | NumberBox | 水平平移 | −1 到 1，按画面宽度比例。 | 默认 0（C# 默认）；范围 –，步进  | Pan horizontally — −1 to 1 as a fraction of picture width. | SettingsCatalog.cs:472 → `AppSettings.VideoPanX` |
| `video.pan-y` | NumberBox | 垂直平移 | −1 到 1，按画面高度比例。 | 默认 0（C# 默认）；范围 –，步进  | Pan vertically — −1 to 1 as a fraction of picture height. | SettingsCatalog.cs:479 → `AppSettings.VideoPanY` |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.reset` | Button | 还原画面设置 | 比例、裁剪、旋转、翻转、缩放与平移恢复默认。 | 动作 `reset-video`（SettingsWindow.xaml.cs:1037） | Reset picture settings — Restore aspect, crop, rotation, flips, zoom, and pan to defaults. | SettingsCatalog.cs:486 |

### 页面：音频（`Audio`，11 行，图标 U+E767）

页面说明：音量上限、延迟与输出设备

English：Audio — Volume limits, delay, and output device

#### 分节：音量与输出 / Volume & Output

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.volume` | NumberBox | 音量 | 0–200%；超过 100% 时 OSC 会画出增益缺口。 | 默认 100；范围 0–200，步进 1 % | Volume — 0–200%; above 100% the OSC draws a gain notch. | SettingsCatalog.cs:493 → `AppSettings.Volume` |
| `audio.max-volume` | NumberBox | 音量上限 | 限制音量滑条与滚轮能到达的最大值。 | 默认 130；范围 100–200，步进 5 % | Volume limit — Maximum value reachable by the slider and mouse wheel. | SettingsCatalog.cs:500 → `AppSettings.MaxVolume` |
| `audio.mute` | ToggleSwitch | 静音 | 保持音量值不变，仅关闭输出。 | 默认 false（C# 默认） | Mute — Keep the volume value unchanged while muting output. | SettingsCatalog.cs:507 → `AppSettings.Muted` |
#### 分节：延迟 / Delay

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.delay` | NumberBox | 音频延迟 | 正值延后音频，负值提前；−10 到 10 秒。 | 默认 0（C# 默认）；范围 -10–10，步进 0.1 秒 | Audio delay — Positive delays audio, negative advances it; −10 to 10 seconds. | SettingsCatalog.cs:513 → `AppSettings.AudioDelay` |
#### 分节：音量与输出 / Volume & Output

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.device` | ComboBox（音频设备） | 音频输出设备 | 从 libmpv 检测到的输出设备中选择；默认跟随系统默认设备，切换后立即生效。 | 默认 "auto" | Audio output device — Pick one of the devices libmpv reports; the system default is used unless you choose another one. Changes apply immediately. | SettingsCatalog.cs:520 → `AppSettings.AudioDevice` |
| `audio.auto-external` | ToggleSwitch | 自动加载外挂音轨 | 自动加载与视频同名的 .mka 音轨；不会加载无关音频文件。 | 默认 true | Auto-load external audio — Automatically load a same-name .mka next to the video; unrelated audio files are never loaded. | SettingsCatalog.cs:526 → `AppSettings.AutoLoadExternalAudio` |
#### 分节：均衡器 / Equalizer

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.equalizer` | ToggleSwitch | 启用 10 段均衡器 | 31 Hz–16 kHz，每段 −12…+12 dB；实时写入 mpv af。 | 默认 false（C# 默认） | Enable 10-band equalizer — 31 Hz–16 kHz with −12…+12 dB per band; written to mpv af in real time. | SettingsCatalog.cs:532 → `AppSettings.EqualizerEnabled` |
| `audio.equalizer-preset` | ComboBox | 均衡器预设 | 选择预设会立即覆盖十段增益；手动调整后变为“自定义”。 | 默认 "Flat"；选项： | Equalizer preset — Choosing a preset overwrites all ten gains; manual edits switch to \"Custom\". | SettingsCatalog.cs:538 → `AppSettings.EqualizerPreset` |
| `audio.equalizer-reset` | Button | 重置均衡器 | 十段增益归零并选择 Flat；启用状态保持不变。 | 动作 `reset-equalizer`（SettingsWindow.xaml.cs:1097） | Reset equalizer — Set all ten gains to zero and select Flat; the enabled state is unchanged. | SettingsCatalog.cs:547 |
#### 分节：滤镜 / Filters

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.filters` | Button | 打开音频滤镜… | 响度标准化、动态音量、均衡器与命名预设；不支持变调，原因见窗口。 | 动作 `open-audio-filters`（SettingsWindow.xaml.cs:1093） | Open audio filters… — Loudness normalization, dynamic volume, equalizer, and named presets; pitch shift is not supported (see the window for details). | SettingsCatalog.cs:552 |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.reset` | Button | 还原音频设置 | 音量、静音、延迟与设备恢复默认。 | 动作 `reset-audio`（SettingsWindow.xaml.cs:1043） | Reset audio settings — Restore volume, mute, delay, and device to defaults. | SettingsCatalog.cs:557 |

### 页面：字幕（`Subtitles`，15 行，图标 U+E8D8）

页面说明：字体、颜色、位置与延迟

English：Subtitles — Font, color, position, and delay

#### 分节：外观 / Appearance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.visible` | ToggleSwitch | 显示字幕 | 关闭后仍保留字幕轨道，只是不渲染。 | 默认 true | Show subtitles — Turning this off keeps the subtitle track but stops rendering it. | SettingsCatalog.cs:564 → `AppSettings.SubtitleVisible` |
#### 分节：时间与位置 / Position & Timing

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.delay` | NumberBox | 字幕延迟 | 正值延后字幕，负值提前；−30 到 30 秒。 | 默认 0（C# 默认）；范围 -30–30，步进 0.1 秒 | Subtitle delay — Positive delays subtitles, negative advances them; −30 to 30 seconds. | SettingsCatalog.cs:570 → `AppSettings.SubtitleDelay` |
| `subtitles.position` | NumberBox | 字幕位置 | 0（底部）到 150（画面上方）。 | 默认 100；范围 –，步进  | Subtitle position — 0 (bottom) to 150 (near the top of the picture). | SettingsCatalog.cs:577 → `AppSettings.SubtitlePosition` |
#### 分节：外观 / Appearance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.scale` | NumberBox | 字幕缩放 | 0.5–2.0 倍。 | 默认 1；范围 0.5–2，步进 0.05 × | Subtitle scale — 0.5–2.0×. | SettingsCatalog.cs:584 → `AppSettings.SubtitleScale` |
| `subtitles.font-size` | NumberBox | 字幕字号 | 8–120，对应 mpv sub-font-size。 | 默认 55；范围 –，步进  | Subtitle font size — 8–120, mapped to mpv sub-font-size. | SettingsCatalog.cs:591 → `AppSettings.SubtitleFontSize` |
| `subtitles.font` | 字体按钮 | 字幕字体 | 从系统已安装字体中选择；应用于 mpv sub-font。 | 默认 "Segoe UI" | Subtitle font — Choose an installed system font; applied to mpv sub-font. | SettingsCatalog.cs:598 → `AppSettings.SubtitleFont` |
| `subtitles.color` | ComboBox | 字幕颜色 | 常用颜色；ASS 字幕的内嵌样式优先。 | 默认 "#FFFFFFFF"；选项： | Subtitle color — Common colors; embedded ASS styles take priority. | SettingsCatalog.cs:604 → `AppSettings.SubtitleColor` |
| `subtitles.border` | NumberBox | 字幕边框 | 0–10，对应 mpv sub-border-size。 | 默认 3；范围 –，步进  | Subtitle border — 0–10, mapped to mpv sub-border-size. | SettingsCatalog.cs:619 → `AppSettings.SubtitleBorderSize` |
#### 分节：ASS 字幕 / ASS Subtitles

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.ass-override` | ComboBox | ASS 样式覆盖 | no 使用字幕自带样式；yes/scale/strip/force 强制应用 Wiina 样式。 | 默认 "scale"；选项：使用字幕样式（no） / 覆盖样式（yes） / 缩放（scale） / 剥离（strip） / 强制（force） | ASS style override — no uses the subtitle's own styles; yes/scale/strip/force apply Wiina's style. | SettingsCatalog.cs:626 → `AppSettings.SubtitleAssOverride` |
| `subtitles.bold` | ToggleSwitch | 粗体 | 纯文本与覆盖样式时使用粗体；ASS 内嵌样式仍可能覆盖。 | 默认 false（C# 默认） | Bold — Use bold for plain text and styled overrides; embedded ASS styles can still override. | SettingsCatalog.cs:635 → `AppSettings.SubtitleBold` |
| `subtitles.italic` | ToggleSwitch | 斜体 | 纯文本与覆盖样式时使用斜体；ASS 内嵌样式仍可能覆盖。 | 默认 false（C# 默认） | Italic — Use italic for plain text and styled overrides; embedded ASS styles can still override. | SettingsCatalog.cs:641 → `AppSettings.SubtitleItalic` |
| `subtitles.image-warning` | 只读文本 | 图像字幕提示 | PGS/VobSub 等位图字幕无法调整字体、颜色与边框；检测到图像字幕时快速设置会显示警告。 |  | Image subtitle notice — Bitmap subtitles such as PGS/VobSub cannot change font, color, or border; quick settings shows a warning when they are detected. | SettingsCatalog.cs:647 |
#### 分节：智能加载 / Smart loading

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.smart-autoload` | ToggleSwitch | 智能字幕 | 自动查找视频旁边的字幕文件，并按首选语言选择。 | 默认 true | Smart subtitles — Automatically find sidecar subtitles next to the video and pick the preferred language. | SettingsCatalog.cs:652 → `AppSettings.AutoLoadSmartSubtitles` |
| `subtitles.preferred-language` | ComboBox | 首选字幕语言 | 自动时优先简体中文，其次繁体、英语、日语。 | 默认 SubtitleLanguagePreferences.Auto；选项：自动 / 简体中文 / 繁体中文 / English / 日本語 | Preferred subtitle language — Auto prefers Simplified Chinese, then Traditional Chinese, English, and Japanese. | SettingsCatalog.cs:659 → `AppSettings.PreferredSubtitleLanguage` |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.reset` | Button | 还原字幕设置 | 延迟、位置、缩放、字号、字体、颜色与边框恢复默认。 | 动作 `reset-subtitles`（SettingsWindow.xaml.cs:1049） | Reset subtitle settings — Restore delay, position, scale, size, font, color, and border to defaults. | SettingsCatalog.cs:669 |

### 页面：网络（`Network`，6 行，图标 U+E774）

页面说明：缓存、代理与请求头

English：Network — Cache, proxy, and request headers

#### 分节：缓存 / Cache

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `network.cache` | NumberBox | 前向缓存 | demuxer-max-bytes；网络抖动时更大的缓存更耐卡顿。 | 默认 150；范围 10–2000，步进 10 MiB | Forward cache — demuxer-max-bytes; larger caches tolerate network jitter better. | SettingsCatalog.cs:676 → `AppSettings.CacheSizeMb` |
| `network.cache-back` | NumberBox | 回退缓存 | demuxer-max-back-bytes；用于往回跳转。 | 默认 50；范围 0–2000，步进 10 MiB | Backward cache — demuxer-max-back-bytes; used when seeking backward. | SettingsCatalog.cs:683 → `AppSettings.CacheBackMb` |
#### 分节：网络 / Network

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `network.proxy` | TextBox | HTTP 代理 | 如 http://127.0.0.1:7890；留空表示直连。 | 默认 string.Empty | HTTP proxy — For example http://127.0.0.1:7890; leave empty for a direct connection. | SettingsCatalog.cs:690 → `AppSettings.Proxy` |
| `network.user-agent` | TextBox | User-Agent | 网络流请求头；空值使用 mpv 默认。 | 默认 "Wiina/0.1" | User-Agent — Request header for network streams; empty uses the mpv default. | SettingsCatalog.cs:696 → `AppSettings.UserAgent` |
| `network.timeout` | NumberBox | 网络超时 | 打开/读取流的最长等待秒数。 | 默认 30；范围 5–120，步进 5 秒 | Network timeout — Maximum seconds to wait when opening or reading a stream. | SettingsCatalog.cs:702 → `AppSettings.NetworkTimeoutSeconds` |
| `network.ytdlp` | TextBox | yt-dlp 路径 | 仅使用系统 PATH 或显式路径中的 yt-dlp，不自动下载。 | 默认 string.Empty；**禁用**：yt-dlp 解析在 Stage 10 接入。 | yt-dlp path — Only uses yt-dlp on the system PATH or an explicit path; never downloads it. | SettingsCatalog.cs:709 → `AppSettings.YtDlpPath` |

### 页面：控制（`Control`，8 行，图标 U+E765）

页面说明：鼠标、滚轮与键盘入口

English：Controls — Mouse, wheel, and keyboard entry points

#### 分节：鼠标与滚轮 / Mouse & Wheel

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `control.single-click` | ComboBox | 视频单击动作 | 单击视频区域时执行的动作；双击仍切换全屏。 | 默认 SingleClickAction.HideOsc；选项：隐藏 OSC / 播放 / 暂停 / 全屏 / 无 | Video click action — Action performed when clicking the video area; double-click still toggles fullscreen. | SettingsCatalog.cs:718 → `AppSettings.SingleClickAction` |
| `control.wheel-volume` | NumberBox | 滚轮音量步长 | 视频区域每格滚轮改变的音量。 | 默认 5；范围 1–20，步进 1 % | Wheel volume step — Volume change per wheel notch over the video. | SettingsCatalog.cs:726 → `AppSettings.WheelVolumeStep` |
| `control.wheel-seek` | NumberBox | 滚轮跳转步长 | 水平滚动每格跳转的秒数。 | 默认 5；范围 1–60，步进 1 秒 | Wheel seek step — Seconds seeked per horizontal wheel notch. | SettingsCatalog.cs:733 → `AppSettings.WheelSeekSeconds` |
| `control.invert-wheel` | ToggleSwitch | 反转滚轮方向 | 向上滚降低音量/后退，符合“自然滚动”。 | 默认 false（C# 默认） | Invert wheel direction — Wheel up lowers volume/seeks back, matching natural scrolling. | SettingsCatalog.cs:740 → `AppSettings.InvertWheelDirection` |
#### 分节：键盘 / Keyboard

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `control.keybindings` | Button | 编辑键盘快捷键 | 打开“按键绑定”页录制、清除或导入导出快捷键。 | 动作 `open-keybindings`（SettingsWindow.xaml.cs:1033） | Edit keyboard shortcuts — Open the Key Bindings page to record, clear, import, or export shortcuts. | SettingsCatalog.cs:746 |
| `control.media-keys` | 只读文本 | 系统媒体键 | 已启用：播放/暂停、停止、上一首、下一首与进度拖动。 |  | System media keys — Enabled: play/pause, stop, previous, next, and seek. | SettingsCatalog.cs:751 |
| `control.key-profile` | ComboBox | 键盘方案 | 默认或 IINA；在按键编辑器中修改会创建可命名的自定义方案。 | 默认 KeyBindingProfiles.Default；选项： | Keyboard profile — Choose Default, IINA, or Custom. The IINA profile uses common IINA/mpv shortcuts; edits fork a named custom profile. | SettingsCatalog.cs:757 → `AppSettings.KeyBindingProfile` |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `control.reset` | Button | 还原控制设置 | 鼠标动作、滚轮步长与方向恢复默认。 | 动作 `reset-control`（SettingsWindow.xaml.cs:1055） | Reset control settings — Restore mouse actions, wheel steps, and direction to defaults. | SettingsCatalog.cs:765 |

### 页面：按键绑定（`KeyBindings`，1 行，图标 U+E765）

页面说明：录制、冲突检测与导入导出

English：Key Bindings — Recording, conflict detection, import/export

#### 分节：快捷键 / Shortcuts

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `keybindings.editor` |  | 按键绑定 | 录制新的组合键、处理冲突、导入/导出 JSON；更改立即生效并持久化。 | 默认 "info" | Key bindings — Record new chords, resolve conflicts, and import/export JSON; changes apply immediately and persist. | SettingsCatalog.cs:772 → `AppSettings.MpvLogLevel` |

### 页面：高级（`Advanced`，12 行，图标 U+E7EF）

页面说明：诊断、文件夹与维护

English：Advanced — Diagnostics, folders, and maintenance

#### 分节：诊断 / Diagnostics

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `advanced.mpv-log` | ComboBox | mpv 日志级别 | libmpv 转发到 Wiina 日志的最低级别。 | 默认 "info"；选项：关闭（no） / 信息（info） / 详细（v） / 调试（verbose） | mpv log level — Minimum level forwarded from libmpv to the Wiina log. | SettingsCatalog.cs:784 → `AppSettings.MpvLogLevel` |
| `advanced.renderer` | 只读文本 | 渲染器 | 当前窗口的 gpu-api / gpu-context / hwdec-current。 |  | Renderer — gpu-api / gpu-context / hwdec-current for the current window. | SettingsCatalog.cs:803 |
| `advanced.libmpv` | 只读文本 | libmpv 版本 | 已加载的 libmpv 客户端 API 版本。 |  | libmpv version — Client API version of the loaded libmpv. | SettingsCatalog.cs:808 |
| `advanced.export-diagnostics` | Button | 导出诊断信息 | 把版本、渲染器、关键设置为文本写入日志文件夹。 | 动作 `export-diagnostics`（SettingsWindow.xaml.cs:1112） | Export diagnostics — Write versions, renderer, and key settings as text into the log folder. | SettingsCatalog.cs:813 |
| `utilities.open-url` | Button | 打开 URL… | 校验地址后交给当前播放窗口打开。 | 动作 `open-url-dialog`（SettingsWindow.xaml.cs:1064） | Open URL… — Validate an address and open it in the current playback window. | SettingsCatalog.cs:818 |
#### 分节：文件夹 / Folders

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `advanced.config-folder` | Button | 打开配置文件夹 | %LOCALAPPDATA%\\Wiina（settings.json / mpv 子目录）。 | 动作 `open-config-folder`（SettingsWindow.xaml.cs:1021） |  | SettingsCatalog.cs:823 |
| `general.data-folder` | Button | 打开数据文件夹 | settings.json、history.json 与 mpv 配置所在的目录。 | 动作 `open-data-folder`（SettingsWindow.xaml.cs:1020） | Open data folder — Folder containing settings.json, history.json, and the mpv configuration. | SettingsCatalog.cs:828 |
| `general.screenshot-folder` | Button | 截图文件夹 | Ctrl+S 保存截图的目录（默认 图片\\Wiina Screenshots）。 | 动作 `choose-screenshot-folder`（SettingsWindow.xaml.cs:1000） |  | SettingsCatalog.cs:833 |
| `advanced.log-folder` | Button | 打开日志文件夹 | %LOCALAPPDATA%\\Wiina\\logs 与崩溃日志。 | 动作 `open-log-folder`（SettingsWindow.xaml.cs:1025） |  | SettingsCatalog.cs:840 |
#### 分节：维护 / Maintenance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `advanced.clear-history` | Button | 清空播放历史 | 删除最近文件与断点续播记录。 | 动作 `clear-history`（SettingsWindow.xaml.cs:1118） | Clear playback history — Delete recent files and resume positions. | SettingsCatalog.cs:845 |
| `advanced.clear-thumbnails` | Button | 清空缩略图缓存 | 释放内存中的时间轴缩略图。 | 动作 `clear-thumbnails`（SettingsWindow.xaml.cs:1130） | Clear thumbnail cache — Free timeline thumbnails held in memory. | SettingsCatalog.cs:850 |
| `advanced.reset-all` | Button | 重置所有设置 | 恢复全部默认值（按键绑定、窗口位置与历史不受影响）。 | 动作 `reset-all-settings`（SettingsWindow.xaml.cs:1134） | Reset all settings — Restore every default (key bindings, window position, and history are unaffected). | SettingsCatalog.cs:855 |

### 页面：文件关联（`Integration`，6 行，图标 U+E71B）

页面说明：让 Windows 用 Wiina 打开视频与音频

English：File Associations — Open video and audio with Wiina on Windows

#### 分节：文件关联 / File Associations

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `integration.status` | 只读文本 | 当前关联状态 | 只写入当前用户（HKCU），不需要管理员权限；Windows 仍会要求在“默认应用”中确认。 |  | Current registration — Camera-free per-user registration (HKCU, no administrator rights); Windows still asks you to confirm in Default Apps. | SettingsCatalog.cs:862 |
#### 分节：关联格式 / Formats

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `integration.associate-video` | Button | 关联视频格式 | 把常见视频扩展名注册给 Wiina，并在默认应用中列出。 | 动作 `associate-video`（SettingsWindow.xaml.cs:1152） | Associate video formats — Register common video extensions for Wiina and list it under Default Apps. | SettingsCatalog.cs:868 |
| `integration.associate-audio` | Button | 关联音频格式 | 把常见音频扩展名注册给 Wiina，并在默认应用中列出。 | 动作 `associate-audio`（SettingsWindow.xaml.cs:1153） | Associate audio formats — Register common audio extensions for Wiina and list it under Default Apps. | SettingsCatalog.cs:874 |
| `integration.associate-all` | Button | 关联全部支持格式 | 同时注册视频与音频；重复执行是安全的。 | 动作 `associate-all`（SettingsWindow.xaml.cs:1154） | Associate all supported formats — Register both video and audio; running it again is safe. | SettingsCatalog.cs:880 |
#### 分节：维护 / Maintenance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `integration.unregister` | Button | 取消 Wiina 关联 | 只删除 Wiina 自己写入的 ProgID 与 OpenWith 项，不影响其他播放器。 | 动作 `unregister-associations`（SettingsWindow.xaml.cs:1166） | Remove Wiina associations — Delete only the ProgIDs and OpenWith entries Wiina wrote; other players are untouched. | SettingsCatalog.cs:886 |
| `integration.default-apps` | Button | 在默认应用中确认… | Windows 不允许应用静默抢占默认程序；请在打开的设置页里选择 Wiina。 | 动作 `open-default-apps`（SettingsWindow.xaml.cs:1147） | Confirm in Default Apps… — Windows does not allow an app to silently take over defaults; pick Wiina on the settings page that opens. | SettingsCatalog.cs:892 |

### 页面：插件（`Plugin`，4 行，图标 U+EA86）

页面说明：尚未实现（Stage 9）：仅保留占位与文件夹入口

English：Plugins — Not implemented yet (Stage 9): placeholder and folder entry only

#### 分节：插件 / Plugins

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `plugin.status` | 只读文本 | 插件系统状态 | WebView2 宿主、Info.json 与权限模型在 Stage 9 实装。 |  | Plugin system status — The WebView2 host, Info.json, and permission model ship in Stage 9. | SettingsCatalog.cs:900 |
| `plugin.install` | Button | 安装插件… | 从文件夹或 zip 安装，并展示所需权限。 | 动作 `install-plugin`（SettingsWindow.xaml.cs:1174）；**禁用**：插件加载、权限与 Info.json 解析尚未实现（Stage 9）。 | Install plugin… — Install from a folder or zip and review required permissions. | SettingsCatalog.cs:905 |
| `plugin.folder` | Button | 打开插件文件夹 | %LOCALAPPDATA%\\Wiina\\plugins（当前会创建空目录）。 | 动作 `open-plugin-folder`（SettingsWindow.xaml.cs:1029） |  | SettingsCatalog.cs:911 |
#### 分节：已安装插件 / Installed Plugins

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `plugin.list` |  | 已安装插件 | Stage 9 起显示插件名称、版本、作者与权限。 | 默认 "Segoe UI" | Installed plugins — From Stage 9, shows plugin name, version, author, and permissions. | SettingsCatalog.cs:916 → `AppSettings.SubtitleFont` |

### 页面：工具（`Utilities`，4 行，图标 U+E90F）

页面说明：字体、日志、检查器与关于

English：Utilities — Fonts, logs, inspector, and about

#### 分节：工具 / Tools

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `utilities.font` | 字体按钮 | 字幕字体 | 与“字幕”页共用同一设置，这里提供快速入口。 | 默认 "Segoe UI" | Subtitle font — Shares the setting with the Subtitles page; this is a shortcut. | SettingsCatalog.cs:928 → `AppSettings.SubtitleFont` |
| `utilities.log-viewer` | Button | 日志查看器 | 查看 %TEMP% 跟踪日志与崩溃日志，可刷新、过滤、打开文件夹。 | 动作 `open-log-viewer`（SettingsWindow.xaml.cs:1077） | Log viewer — View %TEMP% trace logs and crash logs; refresh, filter, or open the folder. | SettingsCatalog.cs:934 |
| `utilities.inspector` | Button | 检查器 | 显示当前媒体的文件/视频/音频/字幕技术属性。 | 动作 `open-inspector`（SettingsWindow.xaml.cs:1081） | Inspector — Show file/video/audio/subtitle technical properties for the current media. | SettingsCatalog.cs:939 |
| `utilities.about` | Button | 关于 Wiina | 版本、许可证、mpv 与 WinUI 信息。 | 动作 `open-about`（SettingsWindow.xaml.cs:1108） | About Wiina — Version, license, mpv, and WinUI information. | SettingsCatalog.cs:944 |

## 4. 设置能打开的窗口、对话框与系统页面

### 4.1 窗口一览

| 窗口 | 文件 | 默认尺寸（DIP） | 最小尺寸 | 打开它的设置行 | 用途 |
|---|---|---|---|---|---|
| 画质预设 `VideoQualityWindow` | `src/Wiina.App/Views/VideoQualityWindow.xaml(.cs)` | 840×780 | 720×480 | `video.quality-editor` | 管理 23 项 mpv 画质 profile |
| 视频/音频滤镜 `FiltersWindow` | `src/Wiina.App/Views/FiltersWindow.xaml(.cs)` | 940×680 | 720×480 | `video.filters`、`audio.filters` | 视频滤镜链、音频滤镜、10 段均衡器 |
| 检查器 `InspectorWindow` | `src/Wiina.App/Views/InspectorWindow.xaml(.cs)` | 660×600 | 480×360 | `utilities.inspector` | 当前媒体的文件/视频/音频/字幕技术属性 |
| 日志查看器 `LogViewerWindow` | `src/Wiina.App/Views/LogViewerWindow.xaml(.cs)` | 760×540 | 520×360 | `utilities.log-viewer` | 运行日志、过滤、自动刷新 |
| 快捷键 `KeyBindingWindow` | `src/Wiina.App/Views/KeyBindingWindow.xaml(.cs)` | 880×660（记住上次，限制 760–2000 × 500–1400） | — | `control.keybindings`（`KeyBindings` 页不导航） | 按键录制、冲突检测、导入导出 |
| 关于 `AboutWindow` | `src/Wiina.App/Views/AboutWindow.xaml(.cs)` | 440×430（固定） | 440×430 | `utilities.about` | 版本、许可证、mpv/WinUI 信息 |
| 设置本身 `SettingsWindow` | `src/Wiina.App/Views/SettingsWindow.xaml(.cs)` | 760×600 | 600×440 | 菜单/OSC 菜单/快速设置 | 本文件描述的窗口 |

尺寸来自各窗口构造函数的 `WindowShell.Prepare(...)` 调用：`VideoQualityWindow.xaml.cs:252-253`、
`FiltersWindow.xaml.cs:36-38`、`InspectorWindow.xaml.cs:29-31`、`LogViewerWindow.xaml.cs:22-24`、
`KeyBindingWindow.xaml.cs:24-28`、`AboutWindow.xaml.cs:22-24`、`SettingsWindow.xaml.cs:67-71`。
所有窗口都按 DPI 缩放，深色标题栏按钮由 `WindowShell.ConfigureCaptionButtons` 统一处理。

> `WelcomeWindow`（560×440）由启动流程打开，不在设置里出现，故不列入。

### 4.2 各窗口内部的可操作项

- **画质预设**：顶部 profile `ComboBox` + 新建 / 重命名 / 删除（`VideoQualityWindow.xaml:75-81`）；
  分 7 组、23 个选项：缩放 7（`scale`、`dscale`、放大/缩小抗振铃、`correct-downscaling`、
  `linear-downscaling`、`sigmoid-upscaling`、`scaler-resizes-only`）、色度 2（`cscale`、
  `cscale-antiring`）、抖动 4（`dither`、`dither-depth`、`dither-size-fruit`、
  `temporal-dither`）、去色带 5（`deband-iterations/threshold/range/grain`，总开关在设置页）、
  插帧 2（`interpolation`、`tscale`）、同步 2（`video-sync`、`video-sync-max-video-change`）、
  其他 1（`gpu-dumb-mode`）。9 个数字项是紧凑 `NumberBox`（带步进按钮）。预设值以原始 mpv
  字符串保存，未知键保留为“未识别（保留原值）”。
- **滤镜窗口**：顶栏“视频 / 音频·均衡器”两个标签页 + 关闭；视频页有添加/删除/启用开关/
  上移/下移/清空、滤镜预设保存/加载/删除（`FiltersWindow.xaml:109-144`）；音频页是 10 段
  均衡器（启用、预设、10 个滑块、重置）与音频滤镜链；窗口内含 libmpv 能力提示。
- **检查器**：自动刷新开关、刷新、复制（`InspectorWindow.xaml:27-33`）；内容按文件/视频/
  音频/字幕分组，字段名保持中文技术名。
- **日志查看器**：级别筛选 `ComboBox`、自动刷新开关、刷新、打开文件夹、复制（
  `LogViewerWindow.xaml:19-36`），只读文本框显示 `%LOCALAPPDATA%\Wiina\logs`。
- **快捷键**：窗口标题来自 `keybindings.windowTitle`，内容是搜索框 + 分类列表 + 录制面板
  （按键胶囊、冲突提示、重置/导入/导出），窗口尺寸记忆在
  `AppSettings.KeyBindingWindowWidth/Height`（`AppSettings.Shell2.cs:20-22`）。
- **关于**：图标、版本、许可证摘要、复制信息、关闭（`AboutWindow.xaml:58-63`）；“复制信息”
  把版本/运行时/mpv 信息写入剪贴板。

### 4.3 文件夹选择器、系统页面与对话框

| 触发行 | 行为 | 实现位置 |
|---|---|---|
| `general.screenshot-folder` | `FolderPicker` 选截图目录，写入 `ScreenshotFolder` | `SettingsWindow.xaml.cs:995` |
| `general.data-folder` | 在资源管理器中打开 `%LOCALAPPDATA%\Wiina`（settings.json/history.json） | `:1015` |
| `advanced.config-folder` | 打开 mpv 配置文件所在目录 | `:1016` |
| `advanced.log-folder` | 打开 `%LOCALAPPDATA%\Wiina\logs` | `:1020` |
| `plugin.folder` | 打开（必要时创建）`%LOCALAPPDATA%\Wiina\plugins` | `:1024` |
| `utilities.open-url` | “打开 URL…”对话框：校验 URL + 是否新窗口，然后交给主窗口播放 | `:1059` |
| `advanced.export-diagnostics` | 写 `logs\diagnostics-<时间>.txt` 并打开日志目录 | `App.xaml.cs:694-726`（动作在 `:1107`） |
| `advanced.clear-history` / `clear-thumbnails` / `reset-all` 等 | `ContentDialog` 二次确认后执行 | `:1113` 起 |
| `integration.default-apps` | 用 `Launcher.LaunchUriAsync("ms-settings:defaultapps")` 打开系统默认应用页 | `:1142` |
| `plugin.install` | `ContentDialog` 说明 Stage 9 未实现（禁用行，实际不会触发） | `:1169` |

所有确认/提示对话框都走 `Dialogs` 帮助类（`src/Wiina.App/Dialogs.cs`），沿用 WinUI 默认
ContentDialog 样式；动作失败时 `ShowMessageAsync(`settings.actionFailed`, ex.Message)`
（`SettingsWindow.xaml.cs:1185`）。

## 5. 主窗口快速设置侧栏（与设置页重复的入口）

### 5.1 结构

- 右侧（或左侧）滑出侧栏，默认宽 360 DIP、最小 300（`src/Wiina.Core/State/SidebarState.cs:36-38`）。
- 顶部第一排是**分类页签**：设置 / 播放列表 / 章节 / 插件
  （`src/Wiina.App/Controls/SidebarHost.xaml:139-181`）。
- 选中“设置”后，第二排是**快速设置页签**：布局 / 视频 / 音频 / 字幕（`:190` 起；
  `QuickSettingsPage` 默认 `Video`，`QuickSettingsView.xaml.cs:95,101`，切换在 `:445-457`）。
- 每个页签内部按卡片分组（外观、窗口、OSC、OSD、侧栏、视频轨道、画面、音频、字幕……），
  卡片标题带“重置本卡片”按钮；页面切换有上滑淡入动画（受应用内“界面动画”开关控制）。

### 5.2 快速设置 → 设置项映射

`MainWindow.Sidebar.cs` 的 `OnQuickSettingChanged`（`:491-739`）共 50 个 `QuickSettingKey`
分支，其中 38 个直接写进 `AppSettings` 并立即应用（其余是对轨道/设备/动作的处理）。与设置页
行的对应关系（完整表由脚本生成）：

| 快速设置分支（QuickSettingKey） | 写入的设置项 | 同名设置页行 |
|---|---|---|
| Theme | `AppSettings.Theme` | `general.theme`（General/外观与启动） |
| Animations | `AppSettings.AnimationsEnabled` | `general.animations`（General/外观与启动） |
| Layout | `AppSettings.Layout` | `ui.layout`（Ui/窗口与 OSC） |
| AlwaysOnTop | `AppSettings.AlwaysOnTop` | `ui.always-on-top`（Ui/窗口与 OSC） |
| LockAspect | `AppSettings.LockAspectRatio` | `ui.lock-aspect`（Ui/窗口与 OSC） |
| MpvStats | `AppSettings.ShowMpvStats` | `video.mpv-stats`（Video/解码） |
| OscAutoHide | `AppSettings.OscAutoHide` | `ui.osc-autohide`（Ui/窗口与 OSC） |
| OsdEnabled | `AppSettings.OsdEnabled` | `ui.osd-enabled`（Ui/OSD） |
| OsdPosition | `AppSettings.OsdPosition` | `ui.osd-position`（Ui/OSD） |
| OsdSize | `AppSettings.OsdSizePercent` | `ui.osd-size`（Ui/OSD） |
| OsdDuration | `AppSettings.OsdDurationSeconds` | `ui.osd-duration`（Ui/OSD） |
| FullscreenClock | `AppSettings.FullscreenClockEnabled` | `ui.fullscreen-clock`（Ui/全屏状态栏） |
| FullscreenBattery | `AppSettings.FullscreenBatteryEnabled` | `ui.fullscreen-battery`（Ui/全屏状态栏） |
| FullscreenTitle | `AppSettings.FullscreenTitleEnabled` | `ui.fullscreen-title`（Ui/全屏状态栏） |
| ThumbnailPreview | `AppSettings.ThumbnailPreviewEnabled` | `ui.thumbnail-preview`（Ui/缩略图） |
| Aspect | `AppSettings.VideoAspect` | `video.aspect`（Video/画面） |
| Crop | `AppSettings.VideoCropAspect` | `video.crop`（Video/画面） |
| Rotation | `AppSettings.VideoRotation` | `video.rotation`（Video/画面） |
| FlipHorizontal | `AppSettings.VideoFlipHorizontal` | `video.flip-h`（Video/画面） |
| FlipVertical | `AppSettings.VideoFlipVertical` | `video.flip-v`（Video/画面） |
| Deinterlace | `AppSettings.VideoDeinterlace` | `video.deinterlace`（Video/解码） |
| HardwareDecode | `AppSettings.VideoHardwareDecode` | `video.hwdec`（Video/解码） |
| Zoom | `AppSettings.VideoZoom` | `video.zoom`（Video/画面） |
| PanX | `AppSettings.VideoPanX` | `video.pan-x`（Video/画面） |
| PanY | `AppSettings.VideoPanY` | `video.pan-y`（Video/画面） |
| MaxVolume | `AppSettings.MaxVolume` | `audio.max-volume`（Audio/音量与输出） |
| AudioDelay | `AppSettings.AudioDelay` | `audio.delay`（Audio/延迟） |
| AudioDevice | `AppSettings.AudioDevice` | `audio.device`（Audio/音量与输出） |
| EqualizerEnabled | `AppSettings.EqualizerEnabled` | `audio.equalizer`（Audio/均衡器） |
| EqualizerGain | `AppSettings.EqualizerGains` | —（没有对应行） |
| Mute | `AppSettings.Muted` | `audio.mute`（Audio/音量与输出） |
| SubtitleVisible | `AppSettings.SubtitleVisible` | `subtitles.visible`（Subtitles/外观） |
| SubtitleDelay | `AppSettings.SubtitleDelay` | `subtitles.delay`（Subtitles/时间与位置） |
| SubtitlePosition | `AppSettings.SubtitlePosition` | `subtitles.position`（Subtitles/时间与位置） |
| SubtitleScale | `AppSettings.SubtitleScale` | `subtitles.scale`（Subtitles/外观） |
| SubtitleFontSize | `AppSettings.SubtitleFontSize` | `subtitles.font-size`（Subtitles/外观） |
| SubtitleColor | `AppSettings.SubtitleColor` | `subtitles.color`（Subtitles/外观） |
| SubtitleBorder | `AppSettings.SubtitleBorderSize` | `subtitles.border`（Subtitles/外观） |

### 5.3 与设置页的差异

- 38 个写属性的分支里，37 个与设置页行写的是**同一个** `AppSettings` 属性，但 clamp 与文案各自实现
  （如 `QuickSettings.ClampSubtitleDelay` vs 设置页 `NumberRow` 的 min/max），容易出现两套
  边界；只有 `EqualizerGain` 没有设置页对应行（增益只在滤镜窗口里调）。
- 快速设置**没有**搜索、没有分节标题、没有“恢复默认（整页）”，只有卡片级重置（
  `MainWindow.Sidebar.cs:744` 起 `ResetQuickSettingsCard`）。
- 侧栏本身受“布局（Edge-to-edge/Docked）”“侧栏位置/宽度”“窗口宽度自动收起”影响；
  这些又都在设置页的“界面”里重复出现。

## 6. 现状问题与重构建议（不实现，仅记录）

### 6.1 已确认的问题

| # | 问题 | 证据 |
|---|---|---|
| P1 | **行数口径不一致**：文档长期写 119 行，代码实际 122 行（1.2.4 起先 121，本轮 +1 为 `ui.osc-autohide-seconds`） | `SettingsCatalog.cs` `rows.Add(` ×122；`docs/acceptance/v1.2.5.md` 写 119 |
| P2 | **页面行数严重失衡**：视频 27、界面 21，而按键绑定 1、插件 4、工具 4 | 第 2 节表 |
| P3 | **`KeyBindings` 是“死页”**：在 `Pages` 里但左栏跳过，只能通过控制页动作打开独立窗口 | `SettingsWindow.xaml.cs:114-116` |
| P4 | **分节名跨页重复**：`重置` ×4（视频/音频/字幕/控制）、`滤镜` ×2（视频/音频）、`维护` ×2（高级/文件关联）、`外观` 与 `外观与启动` 语义重叠 | 第 3 节分节标题 |
| P5 | **图标重复**：`Control` 与 `KeyBindings` 同为 `U+E765` | `SettingsCatalog.cs:25-26` |
| P6 | **英文覆盖缺口 4 行**：`general.screenshot-folder`、`advanced.config-folder`、`advanced.log-folder`、`plugin.folder` 在 `SettingsLocalization.EnglishRows` 中缺失（该类注释声称覆盖所有行） | 生成脚本统计 118/122 |
| P7 | **行 id 前缀与所在页不一致**：`general.edge-snap` 在“界面”、`general.data-folder` 在“高级”、`utilities.open-url` 在“高级”；按 id 前缀无法推断页面 | 第 3 节 |
| P8 | **设置/快速设置双入口**：38 个直接写设置项的分支中 37 个与设置行同属性，两套 clamp、两套文案、两处默认值 | 5.2 表 |
| P9 | **重置语义三套**：4 个分页 reset 动作 + 高级 reset-all + 快速设置卡片 reset | `SettingsWindow.xaml.cs:1037-1060`、`:1134`、`MainWindow.Sidebar.cs:744` |
| P10 | **文件关联页信息行与动作行混排**：`integration.status` 是 Info 行，值由运行时 `InfoValue` switch 覆盖（`SettingsWindow.xaml.cs:817`） | 第 4 节 |
| P11 | **插件页是无效导航**：4 行占位（含禁用的 install 动作），用户点进去只能看“未实现” | `Plugin` 页 |
| P12 | **“高级/工具/文件关联”边界模糊**：诊断、文件夹、维护、打开 URL、字体、日志、检查器、关于分散在三页 | 第 2/3 节 |
| P13 | **无“恢复本页默认”**：只有整页 reset 动作（视频/音频/字幕/控制）与全局 reset，界面/通用/网络/高级没有 | 第 3 节 |
| P14 | **默认值表达不一致**：有的来自 `AppSettings` 初始值，有的无显式初始化（显示为 C# 默认）；无统一默认值来源 | 第 3 节“默认”列 |
| P15 | **搜索依赖行 id + keywords**：改 id 会同时影响深链、快速设置、测试与英文覆盖 | `SettingsSearchIndex`、`SettingsLocalization` |
| P16 | **同页分节交错重复**：音频页“音量与输出”被“延迟”拆成两段；字幕页“外观”被“时间与位置”拆成两段，滚动吸附时同一标题出现两次（第 3 节如实保留源码顺序，不是笔误） | 第 3 节 Audio / Subtitles 序列 |

### 6.2 建议的目标结构（仅提案）

| 目标页 | 建议包含 | 来源（现有 id 前缀/分节） |
|---|---|---|
| 外观与主题 | 主题、语言、动画、窗口材质/毛玻璃、恢复窗口位置 | `general.theme/language/animations/restore-window`、`ui.blur-mode` |
| 播放窗口 | 布局、侧栏、OSC、OSD、全屏状态栏、缩略图、边缘吸附 | `ui.*`（除 blur）、`general.edge-snap` |
| 视频 | 解码、HDR/色彩、画面、滤镜、去 Logo、画质预设入口、去色带 | `video.*` |
| 音频 | 音量/输出、延迟、均衡器入口、音频滤镜入口 | `audio.*` |
| 字幕 | 外观、时间/位置、ASS、智能加载 | `subtitles.*` |
| 输入 | 鼠标/滚轮 + 快捷键（行内直接打开编辑器，删掉死页） | `control.*`、`KeyBindings` |
| 网络 | 缓存、代理、请求头 | `network.*` |
| 文件关联 | 状态、视频/音频/全部、取消、默认应用 | `integration.*` |
| 插件 | 暂不显示（或折叠到“高级 → 实验功能”），直到 Stage 9 实现 | `plugin.*` |
| 高级/系统 | 诊断、文件夹、维护、重置、打开 URL | `advanced.*`、`general.data-folder`、`utilities.open-url` |
| 关于 | 移出设置，只保留“关于”窗口入口 | `utilities.about/font/log-viewer/inspector` |

映射规则：**按用户任务分组，而不是按实现模块**；“重置”统一放到“高级 → 重置”，各页只留
“恢复本页默认”链接；快速设置只保留 4 张卡片（外观 / 窗口 / 视频 / 音频），其余改为“更多设置”
深链到对应页。

### 6.3 重构时必须保留的契约

- **行 id 稳定**：菜单/OSC 菜单/欢迎页用 `OpenSettings(page, rowId)` 深链；测试与英文覆盖按
  id 断言。建议重排后保留旧 id 作为别名表。
- **`AppSettings` 属性名稳定**：`settings.json` 直接序列化属性名（无 `JsonNamingPolicy`），改名
  会丢用户设置。
- **`ActionId` 稳定**：脚本/文档/测试引用这些字符串（如 `open-default-apps`、`associate-all`）。
- **loc 键稳定**：行英文覆盖按 id 键控；`loc:Loc` 的 `ui.*` 键用于按钮/标题。
- **保存管线不变**：350 ms 防抖 + `SettingsChanged` 广播；任何新控件都应通过
  `SettingsRowDefinition` 的 get/set，而不是直接写 `App.Settings`。

## 7. 附录 A：全部行 id（按字母序）

| # | 行 id | 页面 / 分节 | 控件 |
|---|---|---|---|
| 1 | `advanced.clear-history` | Advanced / 维护 | Button |
| 2 | `advanced.clear-thumbnails` | Advanced / 维护 | Button |
| 3 | `advanced.config-folder` | Advanced / 文件夹 | Button |
| 4 | `advanced.export-diagnostics` | Advanced / 诊断 | Button |
| 5 | `advanced.libmpv` | Advanced / 诊断 | 只读文本 |
| 6 | `advanced.log-folder` | Advanced / 文件夹 | Button |
| 7 | `advanced.mpv-log` | Advanced / 诊断 | ComboBox |
| 8 | `advanced.renderer` | Advanced / 诊断 | 只读文本 |
| 9 | `advanced.reset-all` | Advanced / 维护 | Button |
| 10 | `audio.auto-external` | Audio / 音量与输出 | ToggleSwitch |
| 11 | `audio.delay` | Audio / 延迟 | NumberBox |
| 12 | `audio.device` | Audio / 音量与输出 | ComboBox（音频设备） |
| 13 | `audio.equalizer` | Audio / 均衡器 | ToggleSwitch |
| 14 | `audio.equalizer-preset` | Audio / 均衡器 | ComboBox |
| 15 | `audio.equalizer-reset` | Audio / 均衡器 | Button |
| 16 | `audio.filters` | Audio / 滤镜 | Button |
| 17 | `audio.max-volume` | Audio / 音量与输出 | NumberBox |
| 18 | `audio.mute` | Audio / 音量与输出 | ToggleSwitch |
| 19 | `audio.reset` | Audio / 重置 | Button |
| 20 | `audio.volume` | Audio / 音量与输出 | NumberBox |
| 21 | `control.invert-wheel` | Control / 鼠标与滚轮 | ToggleSwitch |
| 22 | `control.key-profile` | Control / 键盘 | ComboBox |
| 23 | `control.keybindings` | Control / 键盘 | Button |
| 24 | `control.media-keys` | Control / 键盘 | 只读文本 |
| 25 | `control.reset` | Control / 重置 | Button |
| 26 | `control.single-click` | Control / 鼠标与滚轮 | ComboBox |
| 27 | `control.wheel-seek` | Control / 鼠标与滚轮 | NumberBox |
| 28 | `control.wheel-volume` | Control / 鼠标与滚轮 | NumberBox |
| 29 | `general.animations` | General / 外观与启动 | ToggleSwitch |
| 30 | `general.auto-playlist` | General / 播放列表 | ComboBox |
| 31 | `general.data-folder` | Advanced / 文件夹 | Button |
| 32 | `general.edge-snap` | Ui / 窗口与 OSC | ToggleSwitch |
| 33 | `general.language` | General / 外观与启动 | ComboBox |
| 34 | `general.restore-window` | General / 播放与历史 | ToggleSwitch |
| 35 | `general.resume` | General / 播放与历史 | ToggleSwitch |
| 36 | `general.screenshot-folder` | Advanced / 文件夹 | Button |
| 37 | `general.theme` | General / 外观与启动 | ComboBox |
| 38 | `general.welcome` | General / 外观与启动 | ToggleSwitch |
| 39 | `integration.associate-all` | Integration / 关联格式 | Button |
| 40 | `integration.associate-audio` | Integration / 关联格式 | Button |
| 41 | `integration.associate-video` | Integration / 关联格式 | Button |
| 42 | `integration.default-apps` | Integration / 维护 | Button |
| 43 | `integration.status` | Integration / 文件关联 | 只读文本 |
| 44 | `integration.unregister` | Integration / 维护 | Button |
| 45 | `keybindings.editor` | KeyBindings / 快捷键 |  |
| 46 | `network.cache` | Network / 缓存 | NumberBox |
| 47 | `network.cache-back` | Network / 缓存 | NumberBox |
| 48 | `network.proxy` | Network / 网络 | TextBox |
| 49 | `network.timeout` | Network / 网络 | NumberBox |
| 50 | `network.user-agent` | Network / 网络 | TextBox |
| 51 | `network.ytdlp` | Network / 网络 | TextBox |
| 52 | `plugin.folder` | Plugin / 插件 | Button |
| 53 | `plugin.install` | Plugin / 插件 | Button |
| 54 | `plugin.list` | Plugin / 已安装插件 |  |
| 55 | `plugin.status` | Plugin / 插件 | 只读文本 |
| 56 | `subtitles.ass-override` | Subtitles / ASS 字幕 | ComboBox |
| 57 | `subtitles.bold` | Subtitles / ASS 字幕 | ToggleSwitch |
| 58 | `subtitles.border` | Subtitles / 外观 | NumberBox |
| 59 | `subtitles.color` | Subtitles / 外观 | ComboBox |
| 60 | `subtitles.delay` | Subtitles / 时间与位置 | NumberBox |
| 61 | `subtitles.font` | Subtitles / 外观 | 字体按钮 |
| 62 | `subtitles.font-size` | Subtitles / 外观 | NumberBox |
| 63 | `subtitles.image-warning` | Subtitles / ASS 字幕 | 只读文本 |
| 64 | `subtitles.italic` | Subtitles / ASS 字幕 | ToggleSwitch |
| 65 | `subtitles.position` | Subtitles / 时间与位置 | NumberBox |
| 66 | `subtitles.preferred-language` | Subtitles / 智能加载 | ComboBox |
| 67 | `subtitles.reset` | Subtitles / 重置 | Button |
| 68 | `subtitles.scale` | Subtitles / 外观 | NumberBox |
| 69 | `subtitles.smart-autoload` | Subtitles / 智能加载 | ToggleSwitch |
| 70 | `subtitles.visible` | Subtitles / 外观 | ToggleSwitch |
| 71 | `ui.always-on-top` | Ui / 窗口与 OSC | ToggleSwitch |
| 72 | `ui.blur-mode` | Ui / 窗口与 OSC | ComboBox |
| 73 | `ui.fullscreen-battery` | Ui / 全屏状态栏 | ToggleSwitch |
| 74 | `ui.fullscreen-clock` | Ui / 全屏状态栏 | ToggleSwitch |
| 75 | `ui.fullscreen-title` | Ui / 全屏状态栏 | ToggleSwitch |
| 76 | `ui.layout` | Ui / 窗口与 OSC | ComboBox |
| 77 | `ui.lock-aspect` | Ui / 窗口与 OSC | ToggleSwitch |
| 78 | `ui.osc-autohide` | Ui / 窗口与 OSC | ToggleSwitch |
| 79 | `ui.osc-autohide-seconds` | Ui / 窗口与 OSC | NumberBox |
| 80 | `ui.osc-position` | Ui / 窗口与 OSC | ComboBox |
| 81 | `ui.osd-duration` | Ui / OSD | NumberBox |
| 82 | `ui.osd-enabled` | Ui / OSD | ToggleSwitch |
| 83 | `ui.osd-position` | Ui / OSD | ComboBox |
| 84 | `ui.osd-size` | Ui / OSD | NumberBox |
| 85 | `ui.sidebar-side` | Ui / 窗口与 OSC | ComboBox |
| 86 | `ui.sidebar-width` | Ui / 窗口与 OSC | NumberBox |
| 87 | `ui.thumbnail-bucket` | Ui / 缩略图 | NumberBox |
| 88 | `ui.thumbnail-cache` | Ui / 缩略图 | NumberBox |
| 89 | `ui.thumbnail-preview` | Ui / 缩略图 | ToggleSwitch |
| 90 | `ui.thumbnail-width` | Ui / 缩略图 | NumberBox |
| 91 | `utilities.about` | Utilities / 工具 | Button |
| 92 | `utilities.font` | Utilities / 工具 | 字体按钮 |
| 93 | `utilities.inspector` | Utilities / 工具 | Button |
| 94 | `utilities.log-viewer` | Utilities / 工具 | Button |
| 95 | `utilities.open-url` | Advanced / 诊断 | Button |
| 96 | `video.aspect` | Video / 画面 | ComboBox |
| 97 | `video.crop` | Video / 画面 | ComboBox |
| 98 | `video.deband` | Video / 画面 | ToggleSwitch |
| 99 | `video.deinterlace` | Video / 解码 | ToggleSwitch |
| 100 | `video.delogo-enabled` | Video / 去 Logo | ToggleSwitch |
| 101 | `video.delogo-height` | Video / 去 Logo | NumberBox |
| 102 | `video.delogo-width` | Video / 去 Logo | NumberBox |
| 103 | `video.delogo-x` | Video / 去 Logo | NumberBox |
| 104 | `video.delogo-y` | Video / 去 Logo | NumberBox |
| 105 | `video.filters` | Video / 滤镜 | Button |
| 106 | `video.flip-h` | Video / 画面 | ToggleSwitch |
| 107 | `video.flip-v` | Video / 画面 | ToggleSwitch |
| 108 | `video.frame-drop-osd` | Video / 解码 | ToggleSwitch |
| 109 | `video.hdr-compute-peak` | Video / HDR 与色彩 | ComboBox |
| 110 | `video.hwdec` | Video / 解码 | ToggleSwitch |
| 111 | `video.hwdec-mode` | Video / 解码 | ComboBox |
| 112 | `video.mpv-stats` | Video / 解码 | ToggleSwitch |
| 113 | `video.output-levels` | Video / HDR 与色彩 | ComboBox |
| 114 | `video.pan-x` | Video / 画面 | NumberBox |
| 115 | `video.pan-y` | Video / 画面 | NumberBox |
| 116 | `video.quality` | Video / 画面 | ComboBox |
| 117 | `video.quality-editor` | Video / 画面 | Button |
| 118 | `video.reset` | Video / 重置 | Button |
| 119 | `video.rotation` | Video / 画面 | ComboBox |
| 120 | `video.target-peak` | Video / HDR 与色彩 | NumberBox |
| 121 | `video.tone-mapping` | Video / HDR 与色彩 | ComboBox |
| 122 | `video.zoom` | Video / 画面 | NumberBox |

## 8. 附录 B：本地化键与覆盖

SettingsLocalization.cs：页面英文 12 条、分节英文 33 条、行英文 118 条；行英文键即行 id。

| 页面 id | 中文页名 | English title | 行数 | 覆盖英文行 |
|---|---|---|---|---|
| `General` | 通用 | General | 7 | 7 |
| `Ui` | 界面 | Interface | 21 | 21 |
| `Video` | 视频 | Video | 27 | 27 |
| `Audio` | 音频 | Audio | 11 | 11 |
| `Subtitles` | 字幕 | Subtitles | 15 | 15 |
| `Network` | 网络 | Network | 6 | 6 |
| `Control` | 控制 | Controls | 8 | 8 |
| `KeyBindings` | 按键绑定 | Key Bindings | 1 | 1 |
| `Advanced` | 高级 | Advanced | 12 | 9 |
| `Integration` | 文件关联 | File Associations | 6 | 6 |
| `Plugin` | 插件 | Plugins | 4 | 3 |
| `Utilities` | 工具 | Utilities | 4 | 4 |

未覆盖英文的行：`advanced.config-folder`、`general.screenshot-folder`、`advanced.log-folder`、`plugin.folder`

设置窗口与子窗口的 loc:Loc 键（共 52 个）：

| 键 | 使用窗口 |
|---|---|
| `keybindings.windowTitle` | KeyBindingWindow.xaml |
| `osd.track.video` | FiltersWindow.xaml |
| `ui.about` | WelcomeWindow.xaml |
| `ui.activeVideoFilters` | FiltersWindow.xaml |
| `ui.add` | FiltersWindow.xaml |
| `ui.audioEq` | FiltersWindow.xaml |
| `ui.audioFilters` | FiltersWindow.xaml |
| `ui.audioPreset` | FiltersWindow.xaml |
| `ui.audioPresetExample` | FiltersWindow.xaml |
| `ui.audioPresetPlaceholder` | FiltersWindow.xaml |
| `ui.auto` | InspectorWindow.xaml |
| `ui.autoRefresh` | LogViewerWindow.xaml |
| `ui.changesImmediate` | FiltersWindow.xaml |
| `ui.clear` | WelcomeWindow.xaml |
| `ui.clearShort` | FiltersWindow.xaml |
| `ui.close` | AboutWindow.xaml、FiltersWindow.xaml |
| `ui.copy` | InspectorWindow.xaml、LogViewerWindow.xaml |
| `ui.copyInfo` | AboutWindow.xaml |
| `ui.delete` | FiltersWindow.xaml |
| `ui.deleteSelected` | FiltersWindow.xaml |
| `ui.disabled` | FiltersWindow.xaml |
| `ui.enable` | FiltersWindow.xaml |
| `ui.eq10` | FiltersWindow.xaml |
| `ui.eqRange` | FiltersWindow.xaml |
| `ui.inspector` | InspectorWindow.xaml |
| `ui.libmpvCapabilities` | FiltersWindow.xaml |
| `ui.licenseShort` | AboutWindow.xaml |
| `ui.loadSelected` | FiltersWindow.xaml |
| `ui.manual` | LogViewerWindow.xaml |
| `ui.moveDown` | FiltersWindow.xaml |
| `ui.moveUp` | FiltersWindow.xaml |
| `ui.namedPreset` | FiltersWindow.xaml |
| `ui.noHistory` | WelcomeWindow.xaml |
| `ui.openDataFolder` | WelcomeWindow.xaml |
| `ui.openFile` | WelcomeWindow.xaml |
| `ui.openFileEllipsis` | WelcomeWindow.xaml |
| `ui.openFolder` | LogViewerWindow.xaml |
| `ui.openUrl` | WelcomeWindow.xaml |
| `ui.openUrlShort` | WelcomeWindow.xaml |
| `ui.recent` | WelcomeWindow.xaml |
| `ui.refresh` | InspectorWindow.xaml、LogViewerWindow.xaml |
| `ui.reset` | FiltersWindow.xaml |
| `ui.saveCurrent` | FiltersWindow.xaml |
| `ui.searchSettings` | SettingsWindow.xaml |
| `ui.selectAudioFilter` | FiltersWindow.xaml |
| `ui.selectFilter` | FiltersWindow.xaml |
| `ui.settingsEllipsis` | WelcomeWindow.xaml |
| `ui.toggleEnabled` | FiltersWindow.xaml |
| `ui.videoFilters` | FiltersWindow.xaml |
| `ui.videoPresetExample` | FiltersWindow.xaml |
| `ui.videoPresetPlaceholder` | FiltersWindow.xaml |
| `ui.wiinaSettings` | SettingsWindow.xaml |

> 说明：行级英文键就是行 id（`SettingsLocalization.EnglishRows`）；页面级用 `PageId` 字符串
> （`EnglishPages`），分节级用**中文分节名**做键（`EnglishSections`）。因此改分节中文名会同时
> 影响英文覆盖，需要同步更新字典。`loc:Loc` 的 `ui.*` / `keybindings.*` 键来自
> `UiStringCatalog`，与设置行 id 是两套命名空间。
