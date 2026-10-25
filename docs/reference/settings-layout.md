# Wiina 设置布局快照（1.3.5）

> **快照版本（1.3.5）**：本文是 v1.3.5 工作树的设置界面完整快照：**11 页 / 131 行 / 40 个页面-分节对（35 个不同分节名）/ 571 个本地化键**。
> 与 1.2.7（12 页 / 125 行）相比：缩略图 8 项从「界面」迁到「视频 → 缩略图」，「工具」页并入「高级 → 工具」，
> 字体设置页与 `utilities.font` 移除，`utilities.open-url` 移到「通用 → 播放与历史」，
> `general.screenshot-folder` 移到「视频 → 截图」，新增 `control.media-keys`。完整增删移清单见 §6.4。

> **用途**：设置菜单重排的对照基线。这是一份**静态快照**（非运行时截图）；所有数字与行内容都从源码统计，
> 日期 **2026-10-25**（Asia/Hong_Kong），分支 **`feat/v1.3.5`**。
>
> **覆盖范围**：设置窗口的 11 个页面 / 131 行 / 控件与保存管线、设置能打开的全部窗口与对话框、
> 主窗口快速设置侧栏（含重复入口）、以及仍未解决的布局问题与重构契约。
>
> **不包含**：主窗口菜单、OSC 菜单、欢迎窗口（不由设置打开）、插件系统（未实现）。
>
> **如何重新生成**：以 `src/Wiina.Core/Settings/SettingsCatalog.cs` 的 `BuildRows()` 为准（`rows.Add(` 共 131 处），
> 配合 `SettingsLocalization.cs` 的英文覆盖、`AppSettings*.cs` 的默认值、`SettingsWindow.xaml.cs` 的
> `RunActionAsync` 分支、`MainWindow.Sidebar.cs` 的 `OnQuickSettingChanged` 分支统计。行号均指 1.3.5 工作树（以 id 为准；新增行会使后续行号顺延）。

## 0. 关键数字

| 项目 | 数值 | 来源 |
|---|---|---|
| 设置页面 | **11**（目录 11 项；`KeyBindings` 在页枚举里但左栏跳过，可点击 10 项 + 1 个隐藏页） | `SettingsCatalog.cs:19-29`；`SettingsWindow.xaml.cs` `BuildNavigation` |
| 设置行 | **131** | `SettingsCatalog.cs` `BuildRows()` 内 `rows.Add(` 计数 |
| 页面-分节对 / 分节名 | **40** / **35**（音频「音量与输出」、字幕「外观」各出现两次；`重置` ×4） | 各页 `Section` 序列 |
| 动作行（Button） | **31** | 第 3 节控件列 = Button |
| 行控件种类 | 8 种目录行（Toggle/Choice/Number/Text/AudioDevice/Action/Info/Font）+ 2 种运行时面板（KeyBindings / PluginList） | `SettingsModels.cs` |
| 英文覆盖 | **131 / 131** 行（1.3.1 补齐缺口，1.3.5 仍无缺口） | `SettingsLocalization.cs` |
| 快速设置分支 | **51** 个 `QuickSettingKey`，向 **47** 个不同的 `_settings.*` 属性写入（含 2 个侧栏状态：`SidebarSide`/`SidebarWidth`） | `MainWindow.Sidebar.cs` `OnQuickSettingChanged` |
| 设置窗口默认尺寸 | 760×600 DIP（最小 600×440） | `AppSettings.Shell.cs`；`SettingsWindow.xaml:9-11` |
| 左栏宽度 / 搜索框 | 200 DIP（内容区最小 400）；搜索框 280×32 | `SettingsWindow.xaml:160-186` |

## 1. 全局外壳

### 1.1 窗口与布局

- 窗口 760×600 DIP，最小 600×440；标题栏与设置窗口使用同一套自定义标题栏（`WindowShell`）。
- 左栏固定 200 DIP（`MinWidth=200`），右侧内容区最小 400 DIP；窗口顶部是 48 DIP 的标题行，
  下面是 280×32 的搜索框。
- 左栏按钮为自定义 `ControlTemplate`：**静止态 = 悬停态**（只有 `SecondaryTextBrush` 文字色，
  没有底色/边框变化），仅选中项有强调色底纹；键盘焦点用 2 DIP 强调色描边（`UseSystemFocusVisuals=False`）。
- 导航在 `SettingsWindow.xaml.cs` 的 `BuildNavigation` 中按 `SettingsCatalog.Pages` 生成；`KeyBindings` 被跳过，
  它的编辑器由「控制 → 快捷键」动作行打开。

### 1.2 行控件

| 控件 | 用于 | 行为 |
|---|---|---|
| ToggleSwitch | `BoolRow` | 切换立即生效（防抖保存） |
| ComboBox | `ChoiceRow` | 选项写入枚举/整数 |
| NumberBox | `NumberRow` | 宽 150 / 最小 120，紧凑步进按钮（弹出层）；`ValueChanged` 写入并夹取 |
| TextBox | `TextRow` | 宽 220、字号 12；`TextChanged` 防抖保存 |
| ComboBox + 刷新按钮 | `AudioDeviceRow` | 来自 mpv `audio-device-list`；含「跟随系统默认」与不可用占位 |
| Button | `ActionRow` | 调 `RunActionAsync(actionId)` |
| 只读文本 | `InfoRow` | 运行时状态（文件关联状态、缓存占用、libmpv 版本等） |
| 字体按钮 | `FontRow` | 打开字体选择（1.3.0 起设置页不再有字体行，控件类型保留给滤镜/字幕等旧入口） |
| 快捷键编辑器 | `KeyBindingsRow` | 行内嵌 `KeyBindingsEditor`（1.3.0 重建） |
| 插件列表 | `PluginListRow` | 只读占位（Stage 9 未实现） |

### 1.3 保存与应用管线

1. 控件事件 → `Apply(row, value, immediate)`；非立即项进入 350 ms 防抖计时器。
2. 计时器到期 → `SaveNow()` → `App.SaveSettings()`（`SettingsStore.Save`）→ `SettingsChanged` 广播。
3. 主窗口/播放器订阅 `SettingsChanged`，把变化即时应用到 mpv、OSC、侧栏与主题。
4. 深链：菜单/OSC/欢迎页调用 `App.ShowSettingsWindow(page, rowId)` → `NavigateTo` → 选中左栏页并滚动到目标行。

## 2. 页面总览（顺序 = 左栏顺序）

| # | PageId | 中文 | English | 摘要 | 图标 | 行数 | 分节 |
|---|---|---|---|---|---|---|---|
| 1 | `General` | 通用 | General | 启动、外观与历史记录 | U+E713 | 8 | 外观与启动 / 播放与历史 / 播放列表 |
| 2 | `Ui` | 界面 | Interface | 窗口布局、OSC 与 OSD | U+E790 | 18 | 窗口与 OSC / OSD / 全屏状态栏 |
| 3 | `Video` | 视频 | Video | 解码、缩略图、画质与画面变换 | U+E714 | 39 | 缩略图 / 解码 / HDR 与色彩 / 滤镜 / 去 Logo / 画面 / 截图 / 重置 |
| 4 | `Audio` | 音频 | Audio | 音量上限、延迟与输出设备 | U+E767 | 11 | 音量与输出 / 延迟 / 均衡器 / 滤镜 / 重置 |
| 5 | `Subtitles` | 字幕 | Subtitles | 字体、颜色、位置与延迟 | U+E8D8 | 15 | 外观 / 时间与位置 / ASS 字幕 / 智能加载 / 重置 |
| 6 | `Network` | 网络 | Network | 缓存、代理与请求头 | U+E774 | 6 | 缓存 / 网络 |
| 7 | `Control` | 控制 | Controls | 鼠标、滚轮与键盘入口 | U+E765 | 8 | 鼠标与滚轮 / 键盘 / 重置 |
| 8 | `KeyBindings` | 按键绑定 | Key Bindings | 录制、冲突检测与导入导出 | U+E765 | 1 | 快捷键 |
| 9 | `Advanced` | 高级 | Advanced | 诊断、工具、文件夹与维护 | U+E7EF | 14 | 诊断 / 工具 / 文件夹 / 维护 |
| 10 | `Integration` | 文件关联 | File Associations | 让 Windows 用 Wiina 打开视频与音频 | U+E71B | 7 | 文件关联 / 关联格式 / 维护 |
| 11 | `Plugin` | 插件 | Plugins | 尚未实现（Stage 9）：仅保留占位与文件夹入口 | U+EA86 | 4 | 插件 / 已安装插件 |

> `KeyBindings` 在页枚举里是第 8 页，但左栏会跳过它；实际从「控制 → 快捷键」动作行打开编辑器（见第 4 节）。


## 3. 逐页明细（按源码顺序）

> 每行给出：id、控件、中文标题、描述、默认/范围/选项、English 覆盖、定义位置与写入的 `AppSettings` 属性。
> 「默认」来自 `AppSettings*.cs` 初始化值；「（C# 默认）」表示未显式初始化。

### 页面：通用（`General`，8 行，图标 U+E713）

页面说明：启动、外观与历史记录

English：General — Startup, appearance, and history

#### 分节：外观与启动 / Appearance & Startup

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `general.theme` | ComboBox | 主题 | 深色、浅色或跟随系统；立即应用到所有播放窗口。 | 默认 ThemeMode.Dark；选项：跟随系统 / 深色 / 浅色 | Theme — Dark, light, or follow the system; applied to every playback window immediately. | SettingsCatalog.cs:63 → `AppSettings.Theme` |
| `general.animations` | ToggleSwitch | 界面动画 | 关闭后 OSC、侧栏与 OSD 直接切换，不做淡入淡出。 | 默认 true | Interface animations — When off, OSC, sidebars, and OSD switch instantly without fades. | SettingsCatalog.cs:70 → `AppSettings.AnimationsEnabled` |
| `general.language` | ComboBox | 界面语言 | 选择界面语言；更改立即应用并记住，重启后保持。 | 默认 "system"；选项：跟随系统 / 简体中文 / English | Interface language — Choose the UI language; changes apply immediately and are remembered. | SettingsCatalog.cs:76 → `AppSettings.Language` |
| `general.welcome` | ToggleSwitch | 启动时显示欢迎窗口 | 没有通过命令行/最近文件打开媒体时显示欢迎页。 | 默认 true | Show Welcome on startup — Show the welcome page when no media was opened from the command line or recent list. | SettingsCatalog.cs:83 → `AppSettings.ShowWelcomeWindow` |
#### 分节：播放与历史 / Playback & History

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `general.resume` | ToggleSwitch | 继续上次播放位置 | 打开本地文件时从播放历史断点续播。 | 默认 true | Resume last position — Resume local files from the saved playback history position. | SettingsCatalog.cs:89 → `AppSettings.ResumePlayback` |
| `general.restore-window` | ToggleSwitch | 恢复上次窗口位置 | 启动时还原窗口尺寸与位置；关闭后总是居中显示。 | 默认 true | Restore window position — Restore window size and position at startup; when off, always center the window. | SettingsCatalog.cs:95 → `AppSettings.RestoreWindowPosition` |
| `utilities.open-url` | Button | 打开 URL… | 校验地址后交给当前播放窗口打开。 | 动作 `open-url-dialog`（SettingsWindow.xaml.cs:1075） | Open URL… — Validate an address and open it in the current playback window. | SettingsCatalog.cs:101 |
#### 分节：播放列表 / Playlist

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `general.auto-playlist` | ComboBox | 自动添加播放列表 | 打开单个视频时，自动把同目录的媒体加入播放列表；手动模式只播放选中的文件。 | 默认 AutoPlaylistMode.SameFolder；选项：同目录文件 / 仅手动添加 / 同目录及子文件夹 | Auto-add to playlist — When a single video opens, also queue other media from its folder; manual mode plays only the selected file. | SettingsCatalog.cs:106 → `AppSettings.AutoPlaylistMode` |

### 页面：界面（`Ui`，18 行，图标 U+E790）

页面说明：窗口布局、OSC 与 OSD

English：Interface — Window layout, OSC, and OSD

#### 分节：窗口与 OSC / Window & OSC

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `ui.layout` | ComboBox | 布局 | Edge-to-edge 让视频铺满；Docked 让标题栏和底部 OSC 占位。 | 默认 WindowLayoutMode.EdgeToEdge；选项：Edge-to-edge（覆盖视频） / Docked（视频让路） | Layout — Edge-to-edge fills the window with video; Docked reserves space for the titlebar and bottom OSC. | SettingsCatalog.cs:117 → `AppSettings.Layout` |
| `ui.always-on-top` | ToggleSwitch | 窗口置顶 | 播放窗口始终位于其他窗口之上。 | 默认 false（C# 默认） | Always on top — Keep the playback window above other windows. | SettingsCatalog.cs:126 → `AppSettings.AlwaysOnTop` |
| `ui.lock-aspect` | ToggleSwitch | 锁定窗口宽高比 | 拖动窗口边缘时保持当前视频比例。 | 默认 true | Lock window aspect ratio — Preserve the current video aspect while resizing window edges. | SettingsCatalog.cs:132 → `AppSettings.LockAspectRatio` |
| `general.edge-snap` | ToggleSwitch | 拖动时吸附屏幕边缘 | 拖动窗口时，靠近显示器工作区边缘会实时贴合；拖离时会自动脱离。 | 默认 true | Snap to screen edges — While dragging, snap the window to the nearest work-area edge and pull away automatically, macOS style. | SettingsCatalog.cs:138 → `AppSettings.EdgeSnapping` |
| `ui.sidebar-side` | ComboBox | 侧边栏位置 | 快速设置、播放列表和章节从窗口哪一侧滑入。 | 默认 SidebarSide.Trailing；选项：左侧 / 右侧 | Sidebar side — Which side quick settings, playlist, and chapters slide in from. | SettingsCatalog.cs:145 → `AppSettings.SidebarSide` |
| `ui.sidebar-width` | NumberBox | 侧边栏宽度 | 300–500 DIP；窗口过窄时会自动收起侧边栏。 | 默认 360；范围 300–500，步进 10 DIP | Sidebar width — 300–500 DIP; the sidebar auto-collapses when the window is too narrow. | SettingsCatalog.cs:154 → `AppSettings.SidebarWidth` |
| `ui.osc-position` | ComboBox | OSC 位置 | 浮动、贴底或顶部标题栏内的播放控制条。 | 默认 OscPosition.Floating；选项：浮动 / 底部 / 顶部 | OSC position — Playback controls floating, docked at the bottom, or in the top titlebar. | SettingsCatalog.cs:161 → `AppSettings.OscPosition` |
| `ui.osc-autohide` | ToggleSwitch | OSC 自动隐藏 | 鼠标静止超过下面的延时后淡出；Docked 布局下不隐藏。 | 默认 true | OSC auto-hide — Fade out after the idle delay configured below; stays visible in Docked layout. | SettingsCatalog.cs:168 → `AppSettings.OscAutoHide` |
| `ui.osc-autohide-seconds` | NumberBox | 自动隐藏延时 | 鼠标静止多少秒后隐藏 OSC 与标题栏；0.5–30 秒。 | 默认 2.5；范围 0.5–30，步进 0.5 秒 | Auto-hide delay — Idle seconds before the OSC and titlebar hide (0.5–30 s). | SettingsCatalog.cs:174 → `AppSettings.OscAutoHideSeconds` |
| `ui.osc-prev-next` | ComboBox | OSC 上一首/下一首 | 按钮是跟随播放列表切换，还是在当前文件内快退/快进；按住 Ctrl 时快退/快进 30 秒。 | 默认 OscPrevNextMode.TrackSkip；选项：上一首 / 下一首 / 快退 / 快进 | OSC previous/next buttons — Skip playlist tracks, or seek inside the current file; hold Ctrl to seek 30 seconds. | SettingsCatalog.cs:182 → `AppSettings.OscPrevNextMode` |
| `ui.blur-mode` | ComboBox | 毛玻璃模式 | 固定/动态/始终会抓取视频帧做高斯模糊；关闭后使用不透明纯色 + 重点色染色，性能最好且不透出画面。 | 默认 ChromeBlurMode.Dynamic；选项：固定（静态帧） / 动态（跟随视频） / 始终（持续计算） / 关闭毛玻璃（纯色） | Chrome blur mode — Fixed / Dynamic / Always capture the video frame for a Gaussian blur; Solid uses an opaque panel colour with an accent tint (cheapest, never see-through). | SettingsCatalog.cs:181 → `AppSettings.ChromeBlurMode` |
#### 分节：OSD / OSD

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `ui.osd-enabled` | ToggleSwitch | 显示 OSD 消息 | 音量、跳转、轨道切换等状态提示。 | 默认 true | Show OSD messages — Status hints for volume, seeking, track changes, and more. | SettingsCatalog.cs:188 → `AppSettings.OsdEnabled` |
| `ui.osd-position` | ComboBox | OSD 位置 | 六个位置：左右下角、左右上角、顶部居中或底部居中。 | 默认 OsdPosition.BottomRight；选项：右下 / 左下 / 右上 / 左上 / 顶部居中 / 底部居中 | OSD position — Six placements: bottom/top left or right, plus top or bottom center; the fullscreen clock stays bottom left. | SettingsCatalog.cs:194 → `AppSettings.OsdPosition` |
| `ui.osd-size` | NumberBox | OSD 字号 | 按百分比缩放主文本；1–500%。 | 默认 100；范围 1–500，步进 5 % | OSD font size — Scale the primary text as a percentage; 1–500%. | SettingsCatalog.cs:201 → `AppSettings.OsdSizePercent` |
| `ui.osd-duration` | NumberBox | OSD 停留时间 | 自动隐藏前的停留秒数；隐藏动画固定 0.5 秒。 | 默认 1；范围 0.2–5，步进 0.1 秒 | OSD hold time — Seconds before auto-hide; the hide animation stays at 0.5 seconds. | SettingsCatalog.cs:208 → `AppSettings.OsdDurationSeconds` |
#### 分节：全屏状态栏 / Fullscreen Status Bar

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `ui.fullscreen-clock` | ToggleSwitch | 全屏时钟 | 全屏时在左下角显示时间。 | 默认 true | Fullscreen clock — Show the time in the bottom-left corner in fullscreen. | SettingsCatalog.cs:215 → `AppSettings.FullscreenClockEnabled` |
| `ui.fullscreen-battery` | ToggleSwitch | 全屏电量 | 全屏时在左下角显示电池与充电状态。 | 默认 true | Fullscreen battery — Show battery and charging state in the bottom-left corner in fullscreen. | SettingsCatalog.cs:221 → `AppSettings.FullscreenBatteryEnabled` |
| `ui.fullscreen-title` | ToggleSwitch | 全屏显示标题 | 在左下角状态行中包含当前媒体标题。 | 默认 false（C# 默认） | Show title in fullscreen — Include the current media title in the bottom-left status line. | SettingsCatalog.cs:227 → `AppSettings.FullscreenTitleEnabled` |

### 页面：视频（`Video`，39 行，图标 U+E714）

页面说明：解码、缩略图、画质与画面变换

English：Video — Decoding, thumbnails, picture quality, aspect, crop, and transforms

#### 分节：缩略图 / Thumbnails

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.thumbnail-preview` | ToggleSwitch | 进度条缩略图 | 悬停进度条时显示时间预览与视频帧。 | 默认 true | Timeline thumbnails — Show a time preview and video frame when hovering the progress bar. | SettingsCatalog.cs:234 → `AppSettings.ThumbnailPreviewEnabled` |
| `video.thumbnail-mode` | ComboBox | 缩略图生成模式 | 预生成（推荐）：打开文件后在后台遍历全片，悬停时几乎即时响应；即时预览：不预生成、不写磁盘缓存，指针在进度条上停留一会儿才解码当前时间点，磁盘与 CPU 占用最低。 | 默认 ThumbnailMode.PreGenerate；选项：预生成（推荐） / 即时预览 | Thumbnail generation mode — Pre-generate (recommended) walks the whole timeline in the background so hovering answers almost instantly; Instant never pre-generates and never writes the disk cache, decoding only the hovered time point after a short delay, which keeps disk and CPU use minimal. | SettingsCatalog.cs:249 → `AppSettings.ThumbnailMode` |
| `video.thumbnail-hover-delay` | NumberBox | 缩略图悬停延迟 | 仅即时预览使用：指针在进度条上停留这么久后才开始解码（0–2000 毫秒，步进 50）。快速扫动时只会生成最后停留的位置。 | 默认 250；范围 0–2000，步进 50 ms | Thumbnail hover delay — Instant preview only: how long the pointer must rest on the progress bar before decoding starts (0–2000 ms, step 50). Scrubbing across the bar only generates the position where it stops. | SettingsCatalog.cs:258 → `AppSettings.ThumbnailHoverDelayMs` |
| `video.thumbnail-width` | NumberBox | 缩略图宽度 | 80–320 DIP；高度按视频比例计算。 | 默认 160；范围 80–320，步进 10 DIP | Thumbnail width — 80–320 DIP; height follows the video aspect. | SettingsCatalog.cs:240 → `AppSettings.ThumbnailWidth` |
| `video.thumbnail-bucket` | NumberBox | 缩略图间隔 | 按秒分桶缓存，越大越省内存、精度越低。 | 默认 5；范围 1–30，步进 1 秒 | Thumbnail interval — Cache buckets per second; larger saves memory but is less precise. | SettingsCatalog.cs:247 → `AppSettings.ThumbnailBucketSeconds` |
| `video.thumbnail-cache-mb` | NumberBox | 缩略图磁盘缓存 | 0 关闭磁盘缓存；超出上限时按最近最少使用自动清理。 | 默认 512；范围 0–4096，步进 64 MB | Thumbnail disk cache — Disk budget in megabytes; 0 disables the cache, and least-recently-used frames are evicted when the limit is exceeded. | SettingsCatalog.cs:261 → `AppSettings.ThumbnailCacheMegabytes` |
| `video.thumbnail-slow` | ToggleSwitch | 慢速预览图模式（单核） | 只用一个低优先级工作线程逐张生成，全片不做预生成；低配设备更不容易卡顿，首次悬停会稍慢。 | 默认 false（C# 默认） | Slow thumbnail mode (single core) — Generate frames one at a time on a single low-priority worker and never pre-generate a whole file; weak devices stay responsive at the cost of a slower first hover. | SettingsCatalog.cs:269 → `AppSettings.ThumbnailSlowMode` |
| `video.thumbnail-hint` | 只读文本 | 预生成提示 | 打开文件后会在后台遍历整段视频生成缩略图。低配设备上这可能抢占 CPU 造成短暂卡顿——遇到卡顿就打开上面的“慢速预览图模式（单核）”。 |  | Pre-generation heads-up — After a file opens, Wiina walks the whole timeline in the background to fill the thumbnail cache. On weak devices that can steal CPU and cause brief stutter — turn on \"Slow thumbnail mode (single core)\" if it happens. | SettingsCatalog.cs:276 |
| `video.thumbnail-cache-clear` | Button | 清除缩略图缓存 | 删除磁盘与内存中缓存的缩略图，并报告释放的空间。 | 动作 `clear-thumbnails`（SettingsWindow.xaml.cs:1141） | Clear thumbnail cache — Delete cached thumbnails on disk and in memory, then report the freed space. | SettingsCatalog.cs:282 |
| `video.thumbnail-acceleration` | ComboBox | 预览图硬件加速 | 自动：显卡可用时用 GPU 解码，驱动不支持时由 libmpv 自动回退软解；仅 CPU：强制软解，避免个别驱动花屏或驱动级卡顿。 | 默认 ThumbnailAcceleration.Auto；选项：自动（推荐） / 仅 CPU | Thumbnail hardware acceleration — Auto decodes on the GPU when the hardware decoder works and lets libmpv fall back to software decoding otherwise; "CPU only" forces software decoding for drivers that produce corrupted frames. | SettingsCatalog.cs:285 → `AppSettings.ThumbnailAcceleration` |
#### 分节：解码 / Decoding

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.hwdec` | ToggleSwitch | 硬件解码 | 使用 auto-safe；显卡/驱动不支持时自动回退软解。 | 默认 true | Hardware decoding — Use auto-safe; falls back to software decoding when unsupported. | SettingsCatalog.cs:287 → `AppSettings.VideoHardwareDecode` |
| `video.hwdec-mode` | ComboBox | 硬件解码模式 | 开启硬件解码时使用的 mpv hwdec 模式；auto-safe 最稳妥。 | 默认 "auto-safe"；选项：auto-safe / auto / d3d11va / dxva2 / no | Hardware decoding mode — mpv hwdec mode used when hardware decoding is on; auto-safe is safest. | SettingsCatalog.cs:293 → `AppSettings.VideoHwdecMode` |
| `video.mpv-stats` | ToggleSwitch | 播放统计面板 | 在画面右上角显示解码、渲染、音画同步与丢帧等完整统计。 | 默认 false（C# 默认） | mpv statistics overlay — Show raw mpv frame, render, and playback statistics on screen; useful for diagnosing dropped frames. | SettingsCatalog.cs:303 → `AppSettings.ShowMpvStats` |
| `video.frame-drop-osd` | ToggleSwitch | 丢帧提示 | 丢帧计数增加时在 OSD 中提示。 | 默认 true | Frame-drop OSD — Show a short OSD message whenever the dropped-frame counter increases during playback. | SettingsCatalog.cs:309 → `AppSettings.FrameDropOsd` |
| `video.deinterlace` | ToggleSwitch | 去隔行 | 对隔行扫描素材启用 mpv 去隔行。 | 默认 false（C# 默认） | Deinterlace — Enable mpv deinterlacing for interlaced sources. | SettingsCatalog.cs:315 → `AppSettings.VideoDeinterlace` |
#### 分节：HDR 与色彩 / HDR & Color

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.tone-mapping` | ComboBox | HDR 色调映射 | SDR 屏幕上显示 HDR 内容时使用的 mpv tone-mapping 算法。 | 默认 "auto"；选项：自动 / BT.2390 / ST2094-40 / ST2094-10 / Spline / Hable / Clip / Gamma / Reinhard / Mobius | HDR tone mapping — mpv tone-mapping algorithm used to show HDR content on SDR screens. | SettingsCatalog.cs:321 → `AppSettings.VideoToneMapping` |
| `video.hdr-compute-peak` | ComboBox | HDR 峰值计算 | 自动让 libmpv 根据内容计算峰值亮度；关闭可省一点性能。 | 默认 "auto"；选项：自动 / 开启 / 关闭 | HDR peak detection — Let libmpv compute peak brightness from content; off saves some performance. | SettingsCatalog.cs:341 → `AppSettings.VideoHdrComputePeak` |
| `video.target-peak` | NumberBox | 目标峰值亮度 | 色调映射的目标亮度（nits）；0 表示自动。 | 默认 0（C# 默认）；范围 0–4000，步进 50 nit（0=自动） | Target peak brightness — Target brightness for tone mapping (nits); 0 means automatic. | SettingsCatalog.cs:358 → `AppSettings.VideoTargetPeak` |
| `video.output-levels` | ComboBox | 输出范围 | auto/limited/full；全范围灰阶发灰时可尝试 full。 | 默认 "auto"；选项：自动 / Limited（16–235） / Full（0–255） | Output range — auto/limited/full; try full when full-range grays look washed out. | SettingsCatalog.cs:366 → `AppSettings.VideoOutputLevels` |
#### 分节：滤镜 / Filters

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.filters` | Button | 打开视频滤镜… | 添加/删除/排序/编辑滤镜，保存命名预设并即时应用到 mpv vf。 | 动作 `open-video-filters`（SettingsWindow.xaml.cs:1096） | Open video filters… — Add, remove, reorder, and edit filters, save named presets, and apply them to mpv vf immediately. | SettingsCatalog.cs:375 |
#### 分节：去 Logo / Remove Logo

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.delogo-enabled` | ToggleSwitch | 启用去 Logo | 用周围像素覆盖固定位置的台标；与滤镜列表里的 delogo 互斥。 | 默认 false（C# 默认） | Enable delogo — Cover a fixed logo position with surrounding pixels; mutually exclusive with delogo in the filter list. | SettingsCatalog.cs:380 → `AppSettings.VideoDelogoEnabled` |
| `video.delogo-x` | NumberBox | 去 Logo X | 矩形左上角 X（像素）。 | 默认 0（C# 默认）；范围 0–16384，步进 1 px | Delogo X — X coordinate of the rectangle's top-left corner (pixels). | SettingsCatalog.cs:386 → `AppSettings.VideoDelogoX` |
| `video.delogo-y` | NumberBox | 去 Logo Y | 矩形左上角 Y（像素）。 | 默认 0（C# 默认）；范围 0–16384，步进 1 px | Delogo Y — Y coordinate of the rectangle's top-left corner (pixels). | SettingsCatalog.cs:394 → `AppSettings.VideoDelogoY` |
| `video.delogo-width` | NumberBox | 去 Logo 宽度 | 矩形宽度（像素，至少 2）。 | 默认 320；范围 2–16384，步进 2 px | Delogo width — Rectangle width (pixels, at least 2). | SettingsCatalog.cs:402 → `AppSettings.VideoDelogoWidth` |
| `video.delogo-height` | NumberBox | 去 Logo 高度 | 矩形高度（像素，至少 2）。 | 默认 80；范围 2–16384，步进 2 px | Delogo height — Rectangle height (pixels, at least 2). | SettingsCatalog.cs:410 → `AppSettings.VideoDelogoHeight` |
#### 分节：画面 / Picture

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.quality` | ComboBox | 视频画质预设 | 两个内置档位：默认对齐 mpv/IINA 原生默认（Lanczos 放大、Hermite 缩小、Bilinear 色度）；性能优先为全 Bilinear 的省 GPU 档。23 项细调与导入/导出在“管理画质预设…”里。 | 默认 VideoQualityPreset.Auto；选项：默认 / 性能优先 | Video quality preset — Two built-in profiles: Default matches mpv/IINA defaults (Lanczos upscaling, Hermite downscaling, bilinear chroma); Performance first uses bilinear everywhere for weak GPUs. 23 fine-grained options and import/export live under Manage quality presets…. | SettingsCatalog.cs:418 → `AppSettings.VideoQualityPreset` |
| `video.quality-editor` | Button | 管理画质预设… | 创建、重命名、导入/导出 23 项画质选项（缩放/色度/抖动/去色带参数/插帧/同步/其他）；去色带总开关在下面单独控制。 | 动作 `open-video-quality`（SettingsWindow.xaml.cs:1100） | Manage quality presets… — Create, rename, and import/export 23 quality options (scaling, chroma, dithering, deband parameters, interpolation, sync, other); the deband master toggle is separate below. | SettingsCatalog.cs:435 |
| `video.deband` | ToggleSwitch | 去色带 | 默认关闭；开启后减轻渐变画面的色带，但会轻微柔化细节并增加少量 GPU 开销。 | 默认 false（C# 默认） | Deband — Off by default; reduces colour banding in gradients at the cost of slightly softer detail and a little extra GPU work. | SettingsCatalog.cs:440 → `AppSettings.DebandEnabled` |
| `video.aspect` | ComboBox | 宽高比 | 覆盖视频显示比例；默认跟随容器。 | 默认 "no"；选项： | Aspect ratio — Override the displayed video aspect; by default it follows the container. | SettingsCatalog.cs:446 → `AppSettings.VideoAspect` |
| `video.crop` | ComboBox | 裁剪 | 居中裁剪到指定比例；下次加载视频时重新计算。 | 默认 0（C# 默认）；选项： | Crop — Center-crop to the selected ratio; recalculated the next time a video loads. | SettingsCatalog.cs:461 → `AppSettings.VideoCropAspect` |
| `video.rotation` | ComboBox | 旋转 | 顺时针旋转 90° 的整数倍。 | 默认 0（C# 默认）；选项：0° / 90° / 180° / 270° | Rotation — Rotate clockwise in 90° increments. | SettingsCatalog.cs:475 → `AppSettings.VideoRotation` |
| `video.flip-h` | ToggleSwitch | 水平翻转 | 左右镜像画面。 | 默认 false（C# 默认） | Flip horizontally — Mirror the picture left to right. | SettingsCatalog.cs:482 → `AppSettings.VideoFlipHorizontal` |
| `video.flip-v` | ToggleSwitch | 垂直翻转 | 上下镜像画面。 | 默认 false（C# 默认） | Flip vertically — Mirror the picture top to bottom. | SettingsCatalog.cs:488 → `AppSettings.VideoFlipVertical` |
| `video.zoom` | NumberBox | 缩放 | −1（缩小）到 1（放大）；0 为原始大小。 | 默认 0（C# 默认）；范围 –，步进  | Zoom — −1 (zoom out) to 1 (zoom in); 0 is original size. | SettingsCatalog.cs:494 → `AppSettings.VideoZoom` |
| `video.pan-x` | NumberBox | 水平平移 | −1 到 1，按画面宽度比例。 | 默认 0（C# 默认）；范围 –，步进  | Pan horizontally — −1 to 1 as a fraction of picture width. | SettingsCatalog.cs:501 → `AppSettings.VideoPanX` |
| `video.pan-y` | NumberBox | 垂直平移 | −1 到 1，按画面高度比例。 | 默认 0（C# 默认）；范围 –，步进  | Pan vertically — −1 to 1 as a fraction of picture height. | SettingsCatalog.cs:508 → `AppSettings.VideoPanY` |
#### 分节：截图 / Screenshots

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.screenshot-subtitles` | ToggleSwitch | 截图包含字幕 | 开启时保存当前字幕；关闭时只保存画面。 | 默认 true | Include subtitles in screenshots — On saves the current subtitle track with the frame; off saves the bare video frame. | SettingsCatalog.cs:533 → `AppSettings.ScreenshotWithSubtitles` |
| `general.screenshot-folder` | Button | 截图文件夹 | Ctrl+S 保存截图的目录（默认 图片\\Wiina Screenshots）。 | 动作 `choose-screenshot-folder`（SettingsWindow.xaml.cs:1011） |  | SettingsCatalog.cs:515 |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `video.reset` | Button | 还原画面设置 | 比例、裁剪、旋转、翻转、缩放与平移恢复默认。 | 动作 `reset-video`（SettingsWindow.xaml.cs:1048） | Reset picture settings — Restore aspect, crop, rotation, flips, zoom, and pan to defaults. | SettingsCatalog.cs:522 |

### 页面：音频（`Audio`，11 行，图标 U+E767）

页面说明：音量上限、延迟与输出设备

English：Audio — Volume limits, delay, and output device

#### 分节：音量与输出 / Volume & Output

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.volume` | NumberBox | 音量 | 0–200%；超过 100% 时 OSC 会画出增益缺口。 | 默认 100；范围 0–200，步进 1 % | Volume — 0–200%; above 100% the OSC draws a gain notch. | SettingsCatalog.cs:529 → `AppSettings.Volume` |
| `audio.max-volume` | NumberBox | 音量上限 | 限制音量滑条与滚轮能到达的最大值。 | 默认 130；范围 100–200，步进 5 % | Volume limit — Maximum value reachable by the slider and mouse wheel. | SettingsCatalog.cs:536 → `AppSettings.MaxVolume` |
| `audio.mute` | ToggleSwitch | 静音 | 保持音量值不变，仅关闭输出。 | 默认 false（C# 默认） | Mute — Keep the volume value unchanged while muting output. | SettingsCatalog.cs:543 → `AppSettings.Muted` |
#### 分节：延迟 / Delay

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.delay` | NumberBox | 音频延迟 | 正值延后音频，负值提前；−10 到 10 秒。 | 默认 0（C# 默认）；范围 -10–10，步进 0.1 秒 | Audio delay — Positive delays audio, negative advances it; −10 to 10 seconds. | SettingsCatalog.cs:549 → `AppSettings.AudioDelay` |
#### 分节：音量与输出 / Volume & Output

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.device` | ComboBox（音频设备） | 音频输出设备 | 从 libmpv 检测到的输出设备中选择；默认跟随系统默认设备，切换后立即生效。 | 默认 "auto" | Audio output device — Pick one of the devices libmpv reports; the system default is used unless you choose another one. Changes apply immediately. | SettingsCatalog.cs:556 → `AppSettings.AudioDevice` |
| `audio.auto-external` | ToggleSwitch | 自动加载外挂音轨 | 自动加载与视频同名的 .mka 音轨；不会加载无关音频文件。 | 默认 true | Auto-load external audio — Automatically load a same-name .mka next to the video; unrelated audio files are never loaded. | SettingsCatalog.cs:562 → `AppSettings.AutoLoadExternalAudio` |
#### 分节：均衡器 / Equalizer

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.equalizer` | ToggleSwitch | 启用 10 段均衡器 | 31 Hz–16 kHz，每段 −12…+12 dB；实时写入 mpv af。 | 默认 false（C# 默认） | Enable 10-band equalizer — 31 Hz–16 kHz with −12…+12 dB per band; written to mpv af in real time. | SettingsCatalog.cs:568 → `AppSettings.EqualizerEnabled` |
| `audio.equalizer-preset` | ComboBox | 均衡器预设 | 选择预设会立即覆盖十段增益；手动调整后变为“自定义”。 | 默认 "Flat"；选项： | Equalizer preset — Choosing a preset overwrites all ten gains; manual edits switch to \"Custom\". | SettingsCatalog.cs:574 → `AppSettings.EqualizerPreset` |
| `audio.equalizer-reset` | Button | 重置均衡器 | 十段增益归零并选择 Flat；启用状态保持不变。 | 动作 `reset-equalizer`（SettingsWindow.xaml.cs:1108） | Reset equalizer — Set all ten gains to zero and select Flat; the enabled state is unchanged. | SettingsCatalog.cs:583 |
#### 分节：滤镜 / Filters

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.filters` | Button | 打开音频滤镜… | 响度标准化、动态音量、均衡器与命名预设；不支持变调，原因见窗口。 | 动作 `open-audio-filters`（SettingsWindow.xaml.cs:1104） | Open audio filters… — Loudness normalization, dynamic volume, equalizer, and named presets; pitch shift is not supported (see the window for details). | SettingsCatalog.cs:588 |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `audio.reset` | Button | 还原音频设置 | 音量、静音、延迟与设备恢复默认。 | 动作 `reset-audio`（SettingsWindow.xaml.cs:1054） | Reset audio settings — Restore volume, mute, delay, and device to defaults. | SettingsCatalog.cs:593 |

### 页面：字幕（`Subtitles`，15 行，图标 U+E8D8）

页面说明：字体、颜色、位置与延迟

English：Subtitles — Font, color, position, and delay

#### 分节：外观 / Appearance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.visible` | ToggleSwitch | 显示字幕 | 关闭后仍保留字幕轨道，只是不渲染。 | 默认 true | Show subtitles — Turning this off keeps the subtitle track but stops rendering it. | SettingsCatalog.cs:600 → `AppSettings.SubtitleVisible` |
#### 分节：时间与位置 / Position & Timing

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.delay` | NumberBox | 字幕延迟 | 正值延后字幕，负值提前；−30 到 30 秒。 | 默认 0（C# 默认）；范围 -30–30，步进 0.1 秒 | Subtitle delay — Positive delays subtitles, negative advances them; −30 to 30 seconds. | SettingsCatalog.cs:606 → `AppSettings.SubtitleDelay` |
| `subtitles.position` | NumberBox | 字幕位置 | 0（底部）到 150（画面上方）。 | 默认 100；范围 –，步进  | Subtitle position — 0 (bottom) to 150 (near the top of the picture). | SettingsCatalog.cs:613 → `AppSettings.SubtitlePosition` |
#### 分节：外观 / Appearance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.scale` | NumberBox | 字幕缩放 | 0.5–2.0 倍。 | 默认 1；范围 0.5–2，步进 0.05 × | Subtitle scale — 0.5–2.0×. | SettingsCatalog.cs:620 → `AppSettings.SubtitleScale` |
| `subtitles.font-size` | NumberBox | 字幕字号 | 8–120，对应 mpv sub-font-size。 | 默认 55；范围 –，步进  | Subtitle font size — 8–120, mapped to mpv sub-font-size. | SettingsCatalog.cs:627 → `AppSettings.SubtitleFontSize` |
| `subtitles.font` | 字体按钮 | 字幕字体 | 从系统已安装字体中选择；应用于 mpv sub-font。 | 默认 "Segoe UI" | Subtitle font — Choose an installed system font; applied to mpv sub-font. | SettingsCatalog.cs:634 → `AppSettings.SubtitleFont` |
| `subtitles.color` | ComboBox | 字幕颜色 | 常用颜色；ASS 字幕的内嵌样式优先。 | 默认 "#FFFFFFFF"；选项： | Subtitle color — Common colors; embedded ASS styles take priority. | SettingsCatalog.cs:640 → `AppSettings.SubtitleColor` |
| `subtitles.border` | NumberBox | 字幕边框 | 0–10，对应 mpv sub-border-size。 | 默认 3；范围 –，步进  | Subtitle border — 0–10, mapped to mpv sub-border-size. | SettingsCatalog.cs:655 → `AppSettings.SubtitleBorderSize` |
#### 分节：ASS 字幕 / ASS Subtitles

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.ass-override` | ComboBox | ASS 样式覆盖 | no 使用字幕自带样式；yes/scale/strip/force 强制应用 Wiina 样式。 | 默认 "scale"；选项：使用字幕样式（no） / 覆盖样式（yes） / 缩放（scale） / 剥离（strip） / 强制（force） | ASS style override — no uses the subtitle's own styles; yes/scale/strip/force apply Wiina's style. | SettingsCatalog.cs:662 → `AppSettings.SubtitleAssOverride` |
| `subtitles.bold` | ToggleSwitch | 粗体 | 纯文本与覆盖样式时使用粗体；ASS 内嵌样式仍可能覆盖。 | 默认 false（C# 默认） | Bold — Use bold for plain text and styled overrides; embedded ASS styles can still override. | SettingsCatalog.cs:671 → `AppSettings.SubtitleBold` |
| `subtitles.italic` | ToggleSwitch | 斜体 | 纯文本与覆盖样式时使用斜体；ASS 内嵌样式仍可能覆盖。 | 默认 false（C# 默认） | Italic — Use italic for plain text and styled overrides; embedded ASS styles can still override. | SettingsCatalog.cs:677 → `AppSettings.SubtitleItalic` |
| `subtitles.image-warning` | 只读文本 | 图像字幕提示 | PGS/VobSub 等位图字幕无法调整字体、颜色与边框；检测到图像字幕时快速设置会显示警告。 |  | Image subtitle notice — Bitmap subtitles such as PGS/VobSub cannot change font, color, or border; quick settings shows a warning when they are detected. | SettingsCatalog.cs:683 |
#### 分节：智能加载 / Smart loading

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.smart-autoload` | ToggleSwitch | 智能字幕 | 自动查找视频旁边的字幕文件，并按首选语言选择。 | 默认 true | Smart subtitles — Automatically find sidecar subtitles next to the video and pick the preferred language. | SettingsCatalog.cs:688 → `AppSettings.AutoLoadSmartSubtitles` |
| `subtitles.preferred-language` | ComboBox | 首选字幕语言 | 自动时优先简体中文，其次繁体、英语、日语。 | 默认 SubtitleLanguagePreferences.Auto；选项：自动 / 简体中文 / 繁体中文 / English / 日本語 | Preferred subtitle language — Auto prefers Simplified Chinese, then Traditional Chinese, English, and Japanese. | SettingsCatalog.cs:695 → `AppSettings.PreferredSubtitleLanguage` |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `subtitles.reset` | Button | 还原字幕设置 | 延迟、位置、缩放、字号、字体、颜色与边框恢复默认。 | 动作 `reset-subtitles`（SettingsWindow.xaml.cs:1060） | Reset subtitle settings — Restore delay, position, scale, size, font, color, and border to defaults. | SettingsCatalog.cs:705 |

### 页面：网络（`Network`，6 行，图标 U+E774）

页面说明：缓存、代理与请求头

English：Network — Cache, proxy, and request headers

#### 分节：缓存 / Cache

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `network.cache` | NumberBox | 前向缓存 | demuxer-max-bytes；网络抖动时更大的缓存更耐卡顿。 | 默认 150；范围 10–2000，步进 10 MiB | Forward cache — demuxer-max-bytes; larger caches tolerate network jitter better. | SettingsCatalog.cs:712 → `AppSettings.CacheSizeMb` |
| `network.cache-back` | NumberBox | 回退缓存 | demuxer-max-back-bytes；用于往回跳转。 | 默认 50；范围 0–2000，步进 10 MiB | Backward cache — demuxer-max-back-bytes; used when seeking backward. | SettingsCatalog.cs:719 → `AppSettings.CacheBackMb` |
#### 分节：网络 / Network

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `network.proxy` | TextBox | HTTP 代理 | 如 http://127.0.0.1:7890；留空表示直连。 | 默认 string.Empty | HTTP proxy — For example http://127.0.0.1:7890; leave empty for a direct connection. | SettingsCatalog.cs:726 → `AppSettings.Proxy` |
| `network.user-agent` | TextBox | User-Agent | 网络流请求头；空值使用 mpv 默认。 | 默认 "Wiina/0.1" | User-Agent — Request header for network streams; empty uses the mpv default. | SettingsCatalog.cs:732 → `AppSettings.UserAgent` |
| `network.timeout` | NumberBox | 网络超时 | 打开/读取流的最长等待秒数。 | 默认 30；范围 5–120，步进 5 秒 | Network timeout — Maximum seconds to wait when opening or reading a stream. | SettingsCatalog.cs:738 → `AppSettings.NetworkTimeoutSeconds` |
| `network.ytdlp` | TextBox | yt-dlp 路径 | 仅使用系统 PATH 或显式路径中的 yt-dlp，不自动下载。 | 默认 string.Empty；**禁用**：yt-dlp 解析在 Stage 10 接入。 | yt-dlp path — Only uses yt-dlp on the system PATH or an explicit path; never downloads it. | SettingsCatalog.cs:745 → `AppSettings.YtDlpPath` |

### 页面：控制（`Control`，8 行，图标 U+E765）

页面说明：鼠标、滚轮与键盘入口

English：Controls — Mouse, wheel, and keyboard entry points

#### 分节：鼠标与滚轮 / Mouse & Wheel

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `control.single-click` | ComboBox | 视频单击动作 | 单击视频区域时执行的动作；双击仍切换全屏。 | 默认 SingleClickAction.HideOsc；选项：隐藏 OSC / 播放 / 暂停 / 全屏 / 无 | Video click action — Action performed when clicking the video area; double-click still toggles fullscreen. | SettingsCatalog.cs:754 → `AppSettings.SingleClickAction` |
| `control.wheel-volume` | NumberBox | 滚轮音量步长 | 视频区域每格滚轮改变的音量。 | 默认 5；范围 1–20，步进 1 % | Wheel volume step — Volume change per wheel notch over the video. | SettingsCatalog.cs:762 → `AppSettings.WheelVolumeStep` |
| `control.wheel-seek` | NumberBox | 滚轮跳转步长 | 水平滚动每格跳转的秒数。 | 默认 5；范围 1–60，步进 1 秒 | Wheel seek step — Seconds seeked per horizontal wheel notch. | SettingsCatalog.cs:769 → `AppSettings.WheelSeekSeconds` |
| `control.invert-wheel` | ToggleSwitch | 反转滚轮方向 | 向上滚降低音量/后退，符合“自然滚动”。 | 默认 false（C# 默认） | Invert wheel direction — Wheel up lowers volume/seeks back, matching natural scrolling. | SettingsCatalog.cs:776 → `AppSettings.InvertWheelDirection` |
#### 分节：键盘 / Keyboard

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `control.keybindings` | Button | 编辑键盘快捷键 | 打开“按键绑定”页录制、清除或导入导出快捷键。 | 动作 `open-keybindings`（SettingsWindow.xaml.cs:1044） | Edit keyboard shortcuts — Open the Key Bindings page to record, clear, import, or export shortcuts. | SettingsCatalog.cs:782 |
| `control.media-keys` | ToggleSwitch | 系统媒体键 | 注册 SystemMediaTransportControls，并响应播放/暂停、停止、上一首、下一首与进度拖动；关闭后不再占用媒体键，立即生效。 | 默认 true | System media keys — Register SystemMediaTransportControls and respond to play/pause, stop, previous, next, and seek. Turning this off stops Wiina from claiming the media keys and applies immediately. | SettingsCatalog.cs:787 → `AppSettings.MediaKeysEnabled` |
| `control.key-profile` | ComboBox | 键盘方案 | 内置两套：Wiina 默认（来自随附的 wiina.txt，含 Ctrl+, 打开设置）与 mpv 默认。IINA 配置在 1.3.1 移除，旧值自动迁移到 Wiina。在按键编辑器中修改会创建可命名的自定义方案。 | 默认 Wiina；选项：Wiina 默认 / mpv | Keyboard profile — Two built-ins: Wiina (from the bundled wiina.txt, including Ctrl+, for Settings) and mpv. The IINA profile was removed in 1.3.1 and old values migrate to Wiina; edits fork a named custom profile. | SettingsCatalog.cs:796 → `AppSettings.KeyBindingProfile` |
#### 分节：重置 / Reset

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `control.reset` | Button | 还原控制设置 | 鼠标动作、滚轮步长与方向恢复默认。 | 动作 `reset-control`（SettingsWindow.xaml.cs:1066） | Reset control settings — Restore mouse actions, wheel steps, and direction to defaults. | SettingsCatalog.cs:805 |

### 页面：按键绑定（`KeyBindings`，1 行，图标 U+E765）

页面说明：录制、冲突检测与导入导出

English：Key Bindings — Recording, conflict detection, import/export

#### 分节：快捷键 / Shortcuts

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `keybindings.editor` | 快捷键编辑器 | 按键绑定 | 录制新的组合键、处理冲突、导入/导出 JSON；更改立即生效并持久化。 | 默认 "info" | Key bindings — Record new chords, resolve conflicts, and import/export JSON; changes apply immediately and persist. | SettingsCatalog.cs:812 → `AppSettings.MpvLogLevel` |

### 页面：高级（`Advanced`，14 行，图标 U+E7EF）

页面说明：诊断、工具、文件夹与维护

English：Advanced — Diagnostics, tools, folders, and maintenance

#### 分节：诊断 / Diagnostics

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `advanced.mpv-log` | ComboBox | mpv 日志级别 | libmpv 转发到 Wiina 日志的最低级别。 | 默认 "info"；选项：关闭（no） / 信息（info） / 详细（v） / 调试（verbose） | mpv log level — Minimum level forwarded from libmpv to the Wiina log. | SettingsCatalog.cs:824 → `AppSettings.MpvLogLevel` |
| `advanced.renderer` | 只读文本 | 渲染器 | 当前窗口的 gpu-api / gpu-context / hwdec-current。 |  | Renderer — gpu-api / gpu-context / hwdec-current for the current window. | SettingsCatalog.cs:843 |
| `advanced.libmpv` | 只读文本 | libmpv 版本 | 已加载的 libmpv 客户端 API 版本。 |  | libmpv version — Client API version of the loaded libmpv. | SettingsCatalog.cs:848 |
| `advanced.export-diagnostics` | Button | 导出诊断信息 | 把版本、渲染器、关键设置为文本写入日志文件夹。 | 动作 `export-diagnostics`（SettingsWindow.xaml.cs:1123） | Export diagnostics — Write versions, renderer, and key settings as text into the log folder. | SettingsCatalog.cs:853 |
#### 分节：工具 / Tools

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `utilities.log-viewer` | Button | 日志查看器 | 查看 %TEMP% 跟踪日志与崩溃日志，可刷新、过滤、打开文件夹。 | 动作 `open-log-viewer`（SettingsWindow.xaml.cs:1088） | Log viewer — View %TEMP% trace logs and crash logs; refresh, filter, or open the folder. | SettingsCatalog.cs:858 |
| `utilities.inspector` | Button | 检查器 | 显示当前媒体的文件/视频/音频/字幕技术属性。 | 动作 `open-inspector`（SettingsWindow.xaml.cs:1092） | Inspector — Show file/video/audio/subtitle technical properties for the current media. | SettingsCatalog.cs:863 |
| `utilities.about` | Button | 关于 Wiina | 版本、许可证、mpv 与 WinUI 信息。 | 动作 `open-about`（SettingsWindow.xaml.cs:1119） | About Wiina — Version, license, mpv, and WinUI information. | SettingsCatalog.cs:868 |
#### 分节：文件夹 / Folders

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `advanced.config-folder` | Button | 打开配置文件夹 | %LOCALAPPDATA%\\Wiina（settings.json / mpv 子目录）。 | 动作 `open-config-folder`（SettingsWindow.xaml.cs:1032） |  | SettingsCatalog.cs:873 |
| `general.data-folder` | Button | 打开数据文件夹 | settings.json、history.json 与 mpv 配置所在的目录。 | 动作 `open-data-folder`（SettingsWindow.xaml.cs:1031） | Open data folder — Folder containing settings.json, history.json, and the mpv configuration. | SettingsCatalog.cs:878 |
| `advanced.log-folder` | Button | 打开日志文件夹 | %LOCALAPPDATA%\\Wiina\\logs 与崩溃日志。 | 动作 `open-log-folder`（SettingsWindow.xaml.cs:1036） |  | SettingsCatalog.cs:883 |
#### 分节：维护 / Maintenance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `advanced.clear-history` | Button | 清空播放历史 | 删除最近文件与断点续播记录。 | 动作 `clear-history`（SettingsWindow.xaml.cs:1129） | Clear playback history — Delete recent files and resume positions. | SettingsCatalog.cs:888 |
| `advanced.clear-thumbnails` | Button | 清空缩略图缓存 | 释放内存中的时间轴缩略图。 | 动作 `clear-thumbnails`（SettingsWindow.xaml.cs:1141） | Clear thumbnail cache — Free timeline thumbnails held in memory. | SettingsCatalog.cs:893 |
| `advanced.reset-all` | Button | 重置所有设置 | 恢复全部默认值（按键绑定、窗口位置与历史不受影响）。 | 动作 `reset-all-settings`（SettingsWindow.xaml.cs:1145） | Reset all settings — Restore every default (key bindings, window position, and history are unaffected). | SettingsCatalog.cs:898 |
| `advanced.mini-player` | Button | 迷你播放器（音乐模式） | 仍属开发中的功能，可先体验但不保证行为稳定；只保留封面与播放控制。 | 动作 `enter-mini-player`；点击后先弹确认框（默认取消），确认后关闭设置窗再切换 | Enter Mini Player (music mode) — still under development; keep the cover and playback controls. | SettingsCatalog.cs:920 |

### 页面：文件关联（`Integration`，7 行，图标 U+E71B）

页面说明：让 Windows 用 Wiina 打开视频与音频

English：File Associations — Open video and audio with Wiina on Windows

#### 分节：文件关联 / File Associations

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `integration.status` | 只读文本 | 当前关联状态 | Wiina 已写入 Windows 的默认应用注册；下次打开支持的文件并选择 Wiina 时点“始终”，Windows 就会记住。若想让资源管理器直接使用 Wiina，可用下方的“管理员一键关联”。 |  | Current registration — Wiina has already written its Windows default-app registration. The next time you open a supported file and pick Wiina, choose \"Always\" and Windows will remember; use \"Associate as administrator\" below to make Explorer use Wiina directly. | SettingsCatalog.cs:905 |
#### 分节：关联格式 / Formats

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `integration.associate-video` | Button | 关联视频格式 | 把常见视频扩展名注册给 Wiina，并在默认应用中列出。 | 动作 `associate-video`（SettingsWindow.xaml.cs:1163） | Associate video formats — Register common video extensions for Wiina and list it under Default Apps. | SettingsCatalog.cs:911 |
| `integration.associate-audio` | Button | 关联音频格式 | 把常见音频扩展名注册给 Wiina，并在默认应用中列出。 | 动作 `associate-audio`（SettingsWindow.xaml.cs:1164） | Associate audio formats — Register common audio extensions for Wiina and list it under Default Apps. | SettingsCatalog.cs:917 |
| `integration.associate-all` | Button | 关联全部支持格式 | 同时注册视频与音频；重复执行是安全的。 | 动作 `associate-all`（SettingsWindow.xaml.cs:1165） | Associate all supported formats — Register both video and audio; running it again is safe. | SettingsCatalog.cs:923 |
| `integration.associate-admin` | Button | 管理员一键关联… | 写入系统级 ProgID 与扩展名默认值，并清除用户级 UserChoice，让资源管理器直接使用 Wiina。需要管理员权限（UAC）；Windows 仍可能要求再次确认。 | 动作 `associate-admin`（SettingsWindow.xaml.cs:1177） | Associate as administrator… — Write machine-level ProgIDs and extension defaults and clear per-extension UserChoice so Explorer uses Wiina directly. Requires the UAC prompt; Windows may still ask again. | SettingsCatalog.cs:929 |
#### 分节：维护 / Maintenance

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `integration.unregister` | Button | 取消 Wiina 关联 | 只删除 Wiina 自己写入的 ProgID 与 OpenWith 项，不影响其他播放器。 | 动作 `unregister-associations`（SettingsWindow.xaml.cs:1228） | Remove Wiina associations — Delete only the ProgIDs and OpenWith entries Wiina wrote; other players are untouched. | SettingsCatalog.cs:935 |
| `integration.default-apps` | Button | 在默认应用中确认… | Windows 不允许应用静默抢占默认程序；请在打开的设置页里选择 Wiina。 | 动作 `open-default-apps`（SettingsWindow.xaml.cs:1158） | Confirm in Default Apps… — Windows does not allow an app to silently take over defaults; pick Wiina on the settings page that opens. | SettingsCatalog.cs:941 |

### 页面：插件（`Plugin`，4 行，图标 U+EA86）

页面说明：尚未实现（Stage 9）：仅保留占位与文件夹入口

English：Plugins — Not implemented yet (Stage 9): placeholder and folder entry only

#### 分节：插件 / Plugins

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `plugin.status` | 只读文本 | 插件系统状态 | WebView2 宿主、Info.json 与权限模型在 Stage 9 实装。 |  | Plugin system status — The WebView2 host, Info.json, and permission model ship in Stage 9. | SettingsCatalog.cs:949 |
| `plugin.install` | Button | 安装插件… | 从文件夹或 zip 安装，并展示所需权限。 | 动作 `install-plugin`（SettingsWindow.xaml.cs:1236）；**禁用**：插件加载、权限与 Info.json 解析尚未实现（Stage 9）。 | Install plugin… — Install from a folder or zip and review required permissions. | SettingsCatalog.cs:954 |
| `plugin.folder` | Button | 打开插件文件夹 | %LOCALAPPDATA%\\Wiina\\plugins（当前会创建空目录）。 | 动作 `open-plugin-folder`（SettingsWindow.xaml.cs:1040） |  | SettingsCatalog.cs:960 |
#### 分节：已安装插件 / Installed Plugins

| id | 控件 | 标题 | 说明 | 默认 / 范围 / 选项 | English | 来源（定义 → 设置项） |
|---|---|---|---|---|---|---|
| `plugin.list` | 插件列表（只读） | 已安装插件 | Stage 9 起显示插件名称、版本、作者与权限。 |  | Installed plugins — From Stage 9, shows plugin name, version, author, and permissions. | SettingsCatalog.cs:965 |

## 4. 设置能打开的窗口、对话框与系统页面

### 4.1 窗口一览

| 窗口 | 文件 | 默认尺寸（DIP） | 最小尺寸 | 打开它的设置行 | 用途 |
|---|---|---|---|---|---|
| 快捷键编辑器 `KeyBindingsEditor`（内嵌于设置窗口） | `src/Wiina.App/Controls/KeyBindingsEditor.xaml(.cs)` | 行内面板 | — | `control.keybindings`（`KeyBindings` 页不导航） | 搜索/分类、内置只读配置、自定义配置、录制冲突、导入导出 |
| 画质预设 `VideoQualityWindow` | `src/Wiina.App/Views/VideoQualityWindow.xaml(.cs)` | 840×780 | 720×480 | `video.quality-editor` | 管理 23 项 mpv 画质 profile |
| 视频/音频滤镜 `FiltersWindow` | `src/Wiina.App/Views/FiltersWindow.xaml(.cs)` | 940×680 | 720×480 | `video.filters`、`audio.filters` | 视频滤镜链、音频滤镜、10 段均衡器 |
| 检查器 `InspectorWindow` | `src/Wiina.App/Views/InspectorWindow.xaml(.cs)` | 660×600 | 480×360 | `advanced.inspector`（页已并入高级） | 当前媒体的文件/视频/音频/字幕技术属性 |
| 日志查看器 `LogViewerWindow` | `src/Wiina.App/Views/LogViewerWindow.xaml(.cs)` | 760×540 | 520×360 | `advanced.log-viewer` | 运行日志、过滤、自动刷新 |
| 关于 `AboutWindow` | `src/Wiina.App/Views/AboutWindow.xaml(.cs)` | 440×430（固定） | 440×430 | `advanced.about` | 版本、许可证、mpv/WinUI 信息 |
| 设置本身 `SettingsWindow` | `src/Wiina.App/Views/SettingsWindow.xaml(.cs)` | 760×600 | 600×440 | 菜单/OSC 菜单/快速设置 | 本文件描述的窗口 |

> 1.3.0 把原来「工具」页的 4 行并进「高级 → 工具」：行 id 仍是 `utilities.*`，页面显示在高级页。
> `WelcomeWindow`（560×440）由启动流程打开，不在设置里出现，故不列入。

### 4.2 各窗口内部的可操作项

- **快捷键编辑器**（1.3.0 重建，1.3.1 移除 IINA 配置）：可搜索、按分类分组的动作列表；内置配置 Wiina 默认 / mpv 默认
  两者只读；IINA 配置已移除（历史差异见 [IINA 快捷键参考](iina-shortcuts.md)）；自定义配置可新建/重命名/删除；
  录制按键并检测冲突（替换/取消），未保存修改在关闭时拦截；JSON 导入/导出；按配置恢复默认。
- **画质预设**：profile `ComboBox` + 新建/重命名/删除；7 组 23 个 mpv 选项（缩放 7、色度 2、抖动 4、
  去色带 5、插帧 2、同步 2、其他 1），9 个数字项为紧凑 `NumberBox`；去色带总开关在「视频 → 画面」。
- **滤镜窗口**：视频/音频-均衡器两个标签页；添加/删除/启用/上移/下移/清空、预设保存/加载/删除；
  音频页是 10 段均衡器（启用、预设、10 滑块、重置）与音频滤镜链。
- **检查器**：自动刷新开关、刷新、复制；内容按文件/视频/音频/字幕分组，字段名保持中文技术名。
- **日志查看器**：级别筛选、自动刷新、刷新、打开文件夹、复制；只读显示 `%LOCALAPPDATA%\Wiina\logs`。
- **关于**：图标、版本、许可证摘要、复制信息、关闭；「复制信息」把版本/运行时/mpv 信息写入剪贴板。

### 4.3 文件夹选择器、系统页面与对话框（动作 id 与处理行）

| 触发行 | 行为 | `RunActionAsync` 分支 |
|---|---|---|
| `general.screenshot-folder`（现在视频页） | `FolderPicker` 选截图目录，写入 `ScreenshotFolder` | `choose-screenshot-folder`（`SettingsWindow.xaml.cs:1011`） |
| `general.data-folder` | 资源管理器打开 `%LOCALAPPDATA%\Wiina`（settings.json/history.json） | `open-data-folder`（`:1031`） |
| `advanced.config-folder` | 打开 mpv 配置文件所在目录 | `open-config-folder`（`:1032`） |
| `advanced.log-folder` | 打开 `%LOCALAPPDATA%\Wiina\logs` | `open-log-folder`（`:1036`） |
| `plugin.folder` | 打开（必要时创建）`%LOCALAPPDATA%\Wiina\plugins` | `open-plugin-folder`（`:1040`） |
| `control.keybindings` | 内嵌快捷键编辑器（打开按键绑定行） | `open-keybindings`（`:1044`） |
| `utilities.open-url`（现在通用页） | 「打开 URL…」对话框：校验 URL + 是否新窗口，交给主窗口播放 | `open-url-dialog`（`:1075`） |
| `advanced.log-viewer` / `inspector` / `about` | 打开对应窗口 | `:1088` / `:1092` / `:1119` |
| `advanced.export-diagnostics` | 写 `logs\diagnostics-<时间>.txt` 并打开日志目录 | `export-diagnostics`（`:1123`）；`App.ExportDiagnostics` |
| `advanced.clear-history` / `clear-thumbnails` / `reset-all-settings` | `ContentDialog` 二次确认后执行 | `:1129` / `:1141` / `:1145` |
| `integration.default-apps` | `Launcher.LaunchUriAsync("ms-settings:defaultapps")` | `open-default-apps`（`:1158`） |
| `integration.associate-video/audio/all`、`associate-admin`、`unregister-associations` | HKCU 关联 / UAC 提权写 HKLM / 取消关联 | `:1163`–`:1228` |
| `video.filters`、`video.quality-editor`、`audio.filters` | 打开滤镜/画质窗口 | `:1096` / `:1100` / `:1104` |
| `plugin.install` | `ContentDialog` 说明 Stage 9 未实现（禁用行，实际不会触发） | `:1236` |

所有确认/提示对话框都走 `Dialogs` 帮助类（`src/Wiina.App/Dialogs.cs`）；动作失败时显示
`settings.actionFailed` 提示。

## 5. 主窗口快速设置侧栏（与设置页重复的入口）

### 5.1 结构

- 右侧（或左侧）滑出侧栏，默认宽 360 DIP、最小 300（`src/Wiina.Core/State/SidebarState.cs:36-38`）。
- 顶部第一排是**分类页签**：设置 / 播放列表 / 章节 / 插件（`SidebarHost.xaml:139-181`）。
- 选中「设置」后，第二排是**快速设置页签**：布局 / 视频 / 音频 / 字幕（`:190` 起；
  `QuickSettingsPage` 默认 `Video`，`QuickSettingsView.xaml.cs:95,101`）。
- 每个页签内部按卡片分组（外观、窗口、OSC、OSD、侧栏、视频轨道、画面、音频、字幕……），
  卡片标题带「重置本卡片」按钮；切页时滚动位置归零（1.3.0 修复）。
- 1.3.0 曾在「布局」页加过**播放速度档位下拉**（0.5× / 0.75× / 1× / 1.25× / 1.5× / 2× / 2.5× / 3× / 4×）；
  按 1.3.1 的澄清已移除，档位只保留在 OSC 展开菜单 `⋯ → 置顶 → 速度 ▸`，快速设置「视频」页只剩速度滑杆（0–4×）。

### 5.2 快速设置 → 设置项映射

`MainWindow.Sidebar.cs` 的 `OnQuickSettingChanged` 共 **50** 个 `QuickSettingKey` 分支，
向 **44** 个不同的 `_settings.*` 属性写入（其中 40 个是 `AppSettings` 设置项，4 个是侧栏状态），其余分支处理
轨道/设备/动作。写设置项的 40 个分支都会立即应用。与设置页行的对应关系：

| 快速设置分支（QuickSettingKey） | 写入的设置项 | 同名设置页行 |
|---|---|---|
| Theme | `AppSettings.Theme` | `general.theme`（General/外观与启动） |
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
| ThumbnailPreview | `AppSettings.ThumbnailPreviewEnabled` | `video.thumbnail-preview`（Video/缩略图） |
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

- 37 个写属性的分支里，大部分与设置页行写**同一个** `AppSettings` 属性，但 clamp 与文案各自实现
  （如 `QuickSettings.ClampSubtitleDelay` vs 设置页 `NumberRow` 的 min/max），容易出现两套边界；
  只有 `EqualizerGain` 没有设置页对应行（增益只在滤镜窗口里调）。
- 快速设置**没有**搜索、没有分节标题、没有「恢复默认（整页）」，只有卡片级重置
  （`MainWindow.Sidebar.cs` 的 `ResetQuickSettingsCard`）。
- 侧栏本身受「布局（Edge-to-edge/Docked）」「侧栏位置/宽度」「窗口宽度自动收起」影响；
  这些又都在设置页的「界面」里重复出现。

## 6. 1.3.0 / 1.3.1 的变化与仍未解决的问题

### 6.1 1.3.0 已解决的问题（对照 1.2.7）

| 原编号 | 问题 | 1.3.0 处理 |
|---|---|---|
| P1 | 行数口径不一致（旧文档 119/122/125/126/129 混用） | 本快照按源码统计 **131**，并给出可复现方法 |
| P2 | 页面行数失衡（视频 27、界面 21） | 1.3.5：视频 39 / 界面 18（缩略图并入视频），仍失衡但按内容聚拢 |
| P12 | 高级/工具/文件关联边界模糊 | 「工具」页并入「高级 → 工具」；「打开 URL」回通用；截图目录回视频 |
| P13 | 无「恢复本页默认」 | 仍无分页默认，但缩略图整组有独立清理动作（`clear-thumbnails`） |
| 新 | 缩略图设置分散在界面页 | 8 项集中到「视频 → 缩略图」，含占用提示与清理 |
| 新 | 数字输入框点击后空白 | 150 宽 + 紧凑步进按钮修复 |
| 新 | 切页滚动位置不重置 | 侧栏/设置窗口换页归零 |
| 新 | 菜单快捷键显示旧值 | 按当前配置计算实际键位 |

### 6.2 仍未解决的问题

| # | 问题 | 证据 |
|---|---|---|
| R1 | **`KeyBindings` 仍是隐藏页**：在页枚举里但左栏跳过，只能从控制页动作打开 | `SettingsWindow.xaml.cs` `BuildNavigation` |
| R2 | **分节名跨页重复**：`重置` ×4、`外观` 与 `外观与启动` 语义重叠、`维护` ×2、`滤镜` ×2 | 第 3 节分节标题 |
| R3 | **图标重复**：`Control` 与 `KeyBindings` 同为 `U+E765` | `SettingsCatalog.cs` PageList |
| R4（1.3.1 已解决） | **英文覆盖缺口 4 行**：`general.screenshot-folder`、`advanced.config-folder`、`advanced.log-folder`、`plugin.folder` | 统计 122/126 |
| R5 | **行 id 前缀与页面不一致**：`general.edge-snap` 在界面页、`general.data-folder` 在高级页、`utilities.open-url` 在通用页、`utilities.*` 在高级页 | 第 3 节 |
| R6 | **设置/快速设置双入口**：37 个分支与设置行同属性，两套 clamp/文案 | §5.2 |
| R7 | **重置语义三套**：分页 reset + 高级 reset-all + 快速设置卡片 reset | §4.3、§5.3 |
| R8 | **插件页是无效导航**：4 行占位（含禁用 install） | Plugin 页 |
| R9 | **默认值表达不一致**：部分来自 `AppSettings` 初始化，部分无显式初始化 | 第 3 节「默认」列 |
| R10 | **搜索依赖行 id + keywords**：改 id 会同时影响深链、快速设置、测试与英文覆盖 | `SettingsSearchIndex`、`SettingsLocalization` |
| R11 | **同页分节交错重复**：音频「音量与输出」被「延迟」拆成两段；字幕「外观」被「时间与位置」拆成两段（第 3 节如实保留源码顺序） | Audio / Subtitles 序列 |

### 6.3 重构时必须保留的契约

- **行 id 稳定**：菜单/OSC 菜单/欢迎页用 `OpenSettings(page, rowId)` 深链；测试与英文覆盖按 id 断言。
- **`AppSettings` 属性名稳定**：`settings.json` 直接序列化属性名（无 `JsonNamingPolicy`），改名会丢用户设置。
- **`ActionId` 稳定**：脚本/文档/测试引用这些字符串（如 `open-default-apps`、`associate-all`）。
- **loc 键稳定**：行英文覆盖按 id 键控；`loc:Loc` 的 `ui.*` 键用于按钮/标题。
- **保存管线不变**：350 ms 防抖 + `SettingsChanged` 广播；新控件应通过 `SettingsRowDefinition` 的 get/set。

### 6.4 1.2.7 → 1.3.0 增删移清单

| 操作 | 行 id |
|---|---|
| 新增 | `video.thumbnail-preview`、`video.thumbnail-width`、`video.thumbnail-bucket`、`video.thumbnail-cache`、`video.thumbnail-cache-mb`、`video.thumbnail-slow`、`video.thumbnail-hint`、`video.thumbnail-cache-clear`（8 项，替代旧 `ui.thumbnail-*`）、`control.media-keys` |
| 移动 | `utilities.open-url` → General/播放与历史；`general.screenshot-folder` → Video/截图；`utilities.log-viewer`、`utilities.inspector`、`utilities.about` → Advanced/工具 |
| 删除 | `ui.thumbnail-*`（6 行，改名后迁到视频页）、`utilities.font`（字体设置页移除） |


### 6.5 1.3.0 → 1.3.1 增删移清单

| 变化 | 行 id | 说明 |
|---|---|---|
| 新增 | `ui.osc-prev-next` | 界面 / 窗口与 OSC；上一首/下一首 vs 快退/快进（Ctrl = ±30 秒） |
| 新增 | `video.thumbnail-acceleration` | 视频 / 缩略图；自动（推荐）/ 仅 CPU |
| 新增 | `video.screenshot-subtitles` | 视频 / 截图；默认开，三处入口（设置 / OSC 展开菜单 / 快速设置 → 视频） |
| 改名 | `video.thumbnail-cache` | 改为「缩略图缓存下限」，描述改为内存至少保留的张数（实际缓存全片） |
| 行为 | `video.thumbnail-cache-mb` | 仍为磁盘预算；内存缓存不再受此上限约束 |
| 补齐 | 4 行英文 | `general.screenshot-folder`、`advanced.config-folder`、`advanced.log-folder`、`plugin.folder` 已有英文 |
### 6.6 1.3.1 → 1.3.2 增删移清单

| 变化 | 行 id | 说明 |
|---|---|---|
| 删除 | `video.thumbnail-cache` | 内存缓存改为覆盖当前视频全部时间点，不再需要「缓存数量」下限；128 行 |
| 行为 | 缩略图预览总开关 | 关闭时完全不生成：不建 headless mpv、不写磁盘、不做缓存定容；磁盘缓存由「缩略图磁盘缓存」单独控制 |
| 行为 | 截图文件命名 | 由 `wiina-时间戳.png` 改为 `视频名-时-分-秒-毫秒.png`，同名递增、非法字符清洗 |
### 6.7 1.3.2 → 1.3.3 增删移清单

| 变化 | 行 id | 说明 |
|---|---|---|
| 新增 | `advanced.music-mode` | 进入音乐模式；高级 → 模式，动作 `enter-music-mode`，关闭设置窗后切换 |
| 新增 | `advanced.mini-player` | 进入迷你播放器；高级 → 模式，动作 `enter-mini-player` |
| 不变 | 其余行 | 行 id 与顺序未变；菜单不再提供迷你/音乐入口，音频文件也不再自动进入迷你播放器 |

### 6.8 1.3.3 → 1.3.4 增删移清单

| 变化 | 行 id | 说明 |
|---|---|---|
| 删除 | `advanced.music-mode` | 高级 → 模式只保留「迷你播放器（音乐模式）」一行；该行改名后带开发警告，点击弹确认框（默认取消），设置窗先关闭再切换 |
| 改写 | `advanced.mini-player` | 标题「迷你播放器（音乐模式）」；说明含仍属开发中的功能，可先体验但不保证行为稳定 |
| 不变 | 其余行 | 行 id 与顺序未变；丢帧提示改为原生 WinUI 卡片、未聚焦窗口悬停不显示 OSC、PiP 键盘转发均不涉及设置行 |
### 6.9 1.3.4 → 1.3.5 增删移清单

| 变化 | 行 id | 说明 |
|---|---|---|
| 新增 | `video.thumbnail-mode` | 视频 → 缩略图，「预生成（推荐）/ 即时预览」；即时预览不预生成、不读不写磁盘缓存、不按全片定容内存缓存 |
| 新增 | `video.thumbnail-hover-delay` | 视频 → 缩略图，0–2000 ms（步进 50，默认 250），仅即时预览使用 |
| 不变 | 其余行 | 行 id 与顺序未变；毛玻璃矩形、缩略图字幕、小窗深色、吸附均不涉及设置行 |
## 7. 附录 A：全部行 id（按字母序）

> 1.3.5 新增 `video.thumbnail-mode`、`video.thumbnail-hover-delay` 后整体重新编号（共 131 行）；1.3.4 删除的 `advanced.music-mode` 不在表内，1.3.3 新增的 `advanced.mini-player` 仍在表内，1.3.2 删除了 `video.thumbnail-cache`。

| # | 行 id | 页面 / 分节 | 控件 |
|---|---|---|---|
| 1 | `advanced.clear-history` | Advanced / 维护 | Button |
| 2 | `advanced.clear-thumbnails` | Advanced / 维护 | Button |
| 3 | `advanced.config-folder` | Advanced / 文件夹 | Button |
| 4 | `advanced.export-diagnostics` | Advanced / 诊断 | Button |
| 5 | `advanced.libmpv` | Advanced / 诊断 | 只读文本 |
| 6 | `advanced.log-folder` | Advanced / 文件夹 | Button |
| 7 | `advanced.mini-player` | Advanced / 模式 | Button |
| 8 | `advanced.mpv-log` | Advanced / 诊断 | ComboBox |
| 9 | `advanced.renderer` | Advanced / 诊断 | 只读文本 |
| 10 | `advanced.reset-all` | Advanced / 维护 | Button |
| 11 | `audio.auto-external` | Audio / 音量与输出 | ToggleSwitch |
| 12 | `audio.delay` | Audio / 延迟 | NumberBox |
| 13 | `audio.device` | Audio / 音量与输出 | ComboBox（音频设备） |
| 14 | `audio.equalizer` | Audio / 均衡器 | ToggleSwitch |
| 15 | `audio.equalizer-preset` | Audio / 均衡器 | ComboBox |
| 16 | `audio.equalizer-reset` | Audio / 均衡器 | Button |
| 17 | `audio.filters` | Audio / 滤镜 | Button |
| 18 | `audio.max-volume` | Audio / 音量与输出 | NumberBox |
| 19 | `audio.mute` | Audio / 音量与输出 | ToggleSwitch |
| 20 | `audio.reset` | Audio / 重置 | Button |
| 21 | `audio.volume` | Audio / 音量与输出 | NumberBox |
| 22 | `control.invert-wheel` | Control / 鼠标与滚轮 | ToggleSwitch |
| 23 | `control.key-profile` | Control / 键盘 | ComboBox |
| 24 | `control.keybindings` | Control / 键盘 | Button |
| 25 | `control.media-keys` | Control / 键盘 | ToggleSwitch |
| 26 | `control.reset` | Control / 重置 | Button |
| 27 | `control.single-click` | Control / 鼠标与滚轮 | ComboBox |
| 28 | `control.wheel-seek` | Control / 鼠标与滚轮 | NumberBox |
| 29 | `control.wheel-volume` | Control / 鼠标与滚轮 | NumberBox |
| 30 | `general.animations` | General / 外观与启动 | ToggleSwitch |
| 31 | `general.auto-playlist` | General / 播放列表 | ComboBox |
| 32 | `general.data-folder` | Advanced / 文件夹 | Button |
| 33 | `general.edge-snap` | Ui / 窗口与 OSC | ToggleSwitch |
| 34 | `general.language` | General / 外观与启动 | ComboBox |
| 35 | `general.restore-window` | General / 播放与历史 | ToggleSwitch |
| 36 | `general.resume` | General / 播放与历史 | ToggleSwitch |
| 37 | `general.screenshot-folder` | Video / 截图 | Button |
| 38 | `general.theme` | General / 外观与启动 | ComboBox |
| 39 | `general.welcome` | General / 外观与启动 | ToggleSwitch |
| 40 | `integration.associate-admin` | Integration / 关联格式 | Button |
| 41 | `integration.associate-all` | Integration / 关联格式 | Button |
| 42 | `integration.associate-audio` | Integration / 关联格式 | Button |
| 43 | `integration.associate-video` | Integration / 关联格式 | Button |
| 44 | `integration.default-apps` | Integration / 维护 | Button |
| 45 | `integration.status` | Integration / 文件关联 | 只读文本 |
| 46 | `integration.unregister` | Integration / 维护 | Button |
| 47 | `keybindings.editor` | KeyBindings / 快捷键 |  |
| 48 | `network.cache` | Network / 缓存 | NumberBox |
| 49 | `network.cache-back` | Network / 缓存 | NumberBox |
| 50 | `network.proxy` | Network / 网络 | TextBox |
| 51 | `network.timeout` | Network / 网络 | NumberBox |
| 52 | `network.user-agent` | Network / 网络 | TextBox |
| 53 | `network.ytdlp` | Network / 网络 | TextBox |
| 54 | `plugin.folder` | Plugin / 插件 | Button |
| 55 | `plugin.install` | Plugin / 插件 | Button |
| 56 | `plugin.list` | Plugin / 已安装插件 |  |
| 57 | `plugin.status` | Plugin / 插件 | 只读文本 |
| 58 | `subtitles.ass-override` | Subtitles / ASS 字幕 | ComboBox |
| 59 | `subtitles.bold` | Subtitles / ASS 字幕 | ToggleSwitch |
| 60 | `subtitles.border` | Subtitles / 外观 | NumberBox |
| 61 | `subtitles.color` | Subtitles / 外观 | ComboBox |
| 62 | `subtitles.delay` | Subtitles / 时间与位置 | NumberBox |
| 63 | `subtitles.font` | Subtitles / 外观 | 字体按钮 |
| 64 | `subtitles.font-size` | Subtitles / 外观 | NumberBox |
| 65 | `subtitles.image-warning` | Subtitles / ASS 字幕 | 只读文本 |
| 66 | `subtitles.italic` | Subtitles / ASS 字幕 | ToggleSwitch |
| 67 | `subtitles.position` | Subtitles / 时间与位置 | NumberBox |
| 68 | `subtitles.preferred-language` | Subtitles / 智能加载 | ComboBox |
| 69 | `subtitles.reset` | Subtitles / 重置 | Button |
| 70 | `subtitles.scale` | Subtitles / 外观 | NumberBox |
| 71 | `subtitles.smart-autoload` | Subtitles / 智能加载 | ToggleSwitch |
| 72 | `subtitles.visible` | Subtitles / 外观 | ToggleSwitch |
| 73 | `ui.always-on-top` | Ui / 窗口与 OSC | ToggleSwitch |
| 74 | `ui.blur-mode` | Ui / 窗口与 OSC | ComboBox |
| 75 | `ui.fullscreen-battery` | Ui / 全屏状态栏 | ToggleSwitch |
| 76 | `ui.fullscreen-clock` | Ui / 全屏状态栏 | ToggleSwitch |
| 77 | `ui.fullscreen-title` | Ui / 全屏状态栏 | ToggleSwitch |
| 78 | `ui.layout` | Ui / 窗口与 OSC | ComboBox |
| 79 | `ui.lock-aspect` | Ui / 窗口与 OSC | ToggleSwitch |
| 80 | `ui.osc-autohide` | Ui / 窗口与 OSC | ToggleSwitch |
| 81 | `ui.osc-autohide-seconds` | Ui / 窗口与 OSC | NumberBox |
| 82 | `ui.osc-position` | Ui / 窗口与 OSC | ComboBox |
| 83 | `ui.osc-prev-next` | Ui / 窗口与 OSC | ComboBox |
| 84 | `ui.osd-duration` | Ui / OSD | NumberBox |
| 85 | `ui.osd-enabled` | Ui / OSD | ToggleSwitch |
| 86 | `ui.osd-position` | Ui / OSD | ComboBox |
| 87 | `ui.osd-size` | Ui / OSD | NumberBox |
| 88 | `ui.sidebar-side` | Ui / 窗口与 OSC | ComboBox |
| 89 | `ui.sidebar-width` | Ui / 窗口与 OSC | NumberBox |
| 90 | `utilities.about` | Advanced / 工具 | Button |
| 91 | `utilities.inspector` | Advanced / 工具 | Button |
| 92 | `utilities.log-viewer` | Advanced / 工具 | Button |
| 93 | `utilities.open-url` | General / 播放与历史 | Button |
| 94 | `video.aspect` | Video / 画面 | ComboBox |
| 95 | `video.crop` | Video / 画面 | ComboBox |
| 96 | `video.deband` | Video / 画面 | ToggleSwitch |
| 97 | `video.deinterlace` | Video / 解码 | ToggleSwitch |
| 98 | `video.delogo-enabled` | Video / 去 Logo | ToggleSwitch |
| 99 | `video.delogo-height` | Video / 去 Logo | NumberBox |
| 100 | `video.delogo-width` | Video / 去 Logo | NumberBox |
| 101 | `video.delogo-x` | Video / 去 Logo | NumberBox |
| 102 | `video.delogo-y` | Video / 去 Logo | NumberBox |
| 103 | `video.filters` | Video / 滤镜 | Button |
| 104 | `video.flip-h` | Video / 画面 | ToggleSwitch |
| 105 | `video.flip-v` | Video / 画面 | ToggleSwitch |
| 106 | `video.frame-drop-osd` | Video / 解码 | ToggleSwitch |
| 107 | `video.hdr-compute-peak` | Video / HDR 与色彩 | ComboBox |
| 108 | `video.hwdec` | Video / 解码 | ToggleSwitch |
| 109 | `video.hwdec-mode` | Video / 解码 | ComboBox |
| 110 | `video.mpv-stats` | Video / 解码 | ToggleSwitch |
| 111 | `video.output-levels` | Video / HDR 与色彩 | ComboBox |
| 112 | `video.pan-x` | Video / 画面 | NumberBox |
| 113 | `video.pan-y` | Video / 画面 | NumberBox |
| 114 | `video.quality` | Video / 画面 | ComboBox |
| 115 | `video.quality-editor` | Video / 画面 | Button |
| 116 | `video.reset` | Video / 重置 | Button |
| 117 | `video.rotation` | Video / 画面 | ComboBox |
| 118 | `video.screenshot-subtitles` | Video / 截图 | ToggleSwitch |
| 119 | `video.target-peak` | Video / HDR 与色彩 | NumberBox |
| 120 | `video.thumbnail-acceleration` | Video / 缩略图 | ComboBox |
| 121 | `video.thumbnail-bucket` | Video / 缩略图 | NumberBox |
| 122 | `video.thumbnail-cache-clear` | Video / 缩略图 | Button |
| 123 | `video.thumbnail-cache-mb` | Video / 缩略图 | NumberBox |
| 124 | `video.thumbnail-hint` | Video / 缩略图 | 只读文本 |
| 125 | `video.thumbnail-preview` | Video / 缩略图 | ToggleSwitch |
| 126 | `video.thumbnail-mode` | Video / 缩略图 | ComboBox |
| 127 | `video.thumbnail-hover-delay` | Video / 缩略图 | NumberBox |
| 128 | `video.thumbnail-slow` | Video / 缩略图 | ToggleSwitch |
| 129 | `video.thumbnail-width` | Video / 缩略图 | NumberBox |
| 130 | `video.tone-mapping` | Video / HDR 与色彩 | ComboBox |
| 131 | `video.zoom` | Video / 画面 | NumberBox |

## 8. 附录 B：本地化键与覆盖

SettingsLocalization.cs：页面英文 11 条、分节英文 35 条、行英文 131 条；行英文键即行 id。

| 页面 id | 中文页名 | English title | 行数 | 覆盖英文行 |
|---|---|---|---|---|
| `General` | 通用 | General | 8 | 8 |
| `Ui` | 界面 | Interface | 18 | 18 |
| `Video` | 视频 | Video | 39 | 39 |
| `Audio` | 音频 | Audio | 11 | 11 |
| `Subtitles` | 字幕 | Subtitles | 15 | 15 |
| `Network` | 网络 | Network | 6 | 6 |
| `Control` | 控制 | Controls | 8 | 8 |
| `KeyBindings` | 按键绑定 | Key Bindings | 1 | 1 |
| `Advanced` | 高级 | Advanced | 14 | 14 |
| `Integration` | 文件关联 | File Associations | 7 | 7 |
| `Plugin` | 插件 | Plugins | 4 | 4 |

未覆盖英文的行：**无**（1.3.5 为 131 / 131 全部有英文）。1.3.0 缺失的 4 行（`general.screenshot-folder`、`advanced.config-folder`、`advanced.log-folder`、`plugin.folder`）已补齐。

设置窗口与子窗口的 loc:Loc 键（共 54 个）：

| 键 | 使用窗口 |
|---|---|
| `about.title` | AboutWindow.xaml |
| `inspector.title` | InspectorWindow.xaml |
| `keybindings.windowTitle` | KeyBindingWindow.xaml |
| `log.title` | LogViewerWindow.xaml |
| `osd.track.video` | FiltersWindow.xaml |
| `ui.about` | AboutWindow.xaml、WelcomeWindow.xaml |
| `ui.activeVideoFilters` | FiltersWindow.xaml |
| `ui.add` | FiltersWindow.xaml |
| `ui.audioEq` | FiltersWindow.xaml |
| `ui.audioFilters` | FiltersWindow.xaml |
| `ui.audioPreset` | FiltersWindow.xaml |
| `ui.audioPresetExample` | FiltersWindow.xaml |
| `ui.audioPresetPlaceholder` | FiltersWindow.xaml |
| `ui.auto` | InspectorWindow.xaml |
| `ui.autoRefresh` | InspectorWindow.xaml、LogViewerWindow.xaml |
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
| `ui.libmpvCapabilities` | FiltersWindow.xaml |
| `ui.licenseShort` | AboutWindow.xaml |
| `ui.loadSelected` | FiltersWindow.xaml |
| `ui.manual` | InspectorWindow.xaml、LogViewerWindow.xaml |
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
