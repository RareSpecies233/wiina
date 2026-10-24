# Wiina

> Windows 11 原生、IINA 风格界面、mpv 内核的播放器。
> WinUI 3 · Windows App SDK · libmpv · C# / .NET 10

Wiina 把 [IINA](https://iina.io/) 在 macOS 上的**界面逻辑**（窗口层级、OSC、侧边栏、OSD、模式与状态机）带到 Windows，并用
[mpv](https://mpv.io/) 作为播放内核。视觉上不照搬 AppKit，而是按 Windows 11 Fluent 规范重新设计：Segoe UI Variable、
Segoe Fluent Icons、播放器外框的可选视频毛玻璃（纯色档为不透明 accent 染色）、辅助窗口的 Mica/Acrylic、圆角与系统主题。

## ⚠️ Windows SmartScreen / 杀毒软件提示

Wiina 的发布包**没有代码签名**（个人项目无法承担可信证书费用），所以首次运行时可能遇到系统警告：

- **便携版 zip**：从浏览器下载的 zip 会被标记为“来自 Internet”。解压后运行 `Wiina.exe`，如果出现
  “Windows 已保护你的电脑”，点 **更多信息 → 仍要运行** 即可。
- **安装包 setup.exe**：同样点 **更多信息 → 仍要运行**。若窗口只给出“不运行”，先右键安装包 →
  属性 → 勾选“解除锁定”再运行。
- **杀毒软件误报**：未签名的 WinUI + libmpv 应用偶尔被启发式引擎标记。Wiina 只读取你打开的媒体，
  不驻留后台、不静默修改系统设置（文件关联只在设置页手动触发）；源码公开可审查：
  <https://github.com/RareSpecies233/wiina>。

**校验下载是否完整、是否被替换**（推荐）：查看 [docs/acceptance/v1.3.2.md](docs/acceptance/v1.3.2.md)
中 zip 与 setup 的 SHA-256，然后在本机对比：

```powershell
Get-FileHash .\wiina-1.3.2-win-x64.zip    -Algorithm SHA256
Get-FileHash .\Wiina-Setup-1.3.2-x64.exe  -Algorithm SHA256
```

两个哈希都与发布记录一致，就说明文件未被篡改或损坏。提示来自“未知发布者”，并不代表检测到病毒；
Wiina 不是恶意软件。

> **English**: The binaries are unsigned, so SmartScreen may show “Windows protected your PC”.
> Click **More info → Run anyway** for both the portable zip and the setup. Verify the SHA-256
> against `docs/acceptance/v1.3.2.md` before running; the source is public at
> <https://github.com/RareSpecies233/wiina>.

## 状态（v1.3.2）

- **Stage 0–8 已验收。** 播放管线、WinUI 标题栏、OSC、播放/轨道/字幕/章节/播放列表/历史、侧边栏与快速设置、
  OSD/缩略图、全屏/PiP/迷你模式/多窗口、11 页设置（含文件关联）/搜索/快捷键/菜单/欢迎与辅助窗口、
  视频滤镜/10 段均衡器/音频滤镜/HDR 与高级音画均已实现。
- **Stage 11（最终打磨与发布）已实现。** 运行时 zh-Hans/en 本地化、无障碍与高对比度、减少动态效果、
  DPI/性能核查、可复现打包、GPL 合规文档。验收记录见
  [docs/acceptance/stage-11.md](docs/acceptance/stage-11.md)。
- **Stage 9（插件）与 Stage 10（在线字幕 / yt-dlp）按维护者要求跳过，未实现。**
  相关菜单项、设置行保持**可见但禁用**并说明原因；本版本不包含插件宿主、WebView2 依赖、
  OpenSubtitles 客户端或 yt-dlp 调用，也不存在“占位可点击”的假功能。
- 历史验收：[Stage 0](docs/acceptance/stage-0.md) · [1](docs/acceptance/stage-1.md) ·
  [2](docs/acceptance/stage-2.md) · [3](docs/acceptance/stage-3.md) · [4](docs/acceptance/stage-4.md) ·
  [5](docs/acceptance/stage-5.md) · [6](docs/acceptance/stage-6.md) · [7](docs/acceptance/stage-7.md) ·
  [8](docs/acceptance/stage-8.md)。

### v1.3.2：光标隐藏修复、全屏修正与 OSC/菜单收尾

- **光标隐藏真正生效**：根因是 WinUI 输入栈（`Microsoft.UI.Input.dll` / `Microsoft.InputStateManager.dll` / `Windows.UI.Immersive.dll`）在输入线程反复调用 `SetCursor` 贴回箭头，应用侧 `SetCursor`/`ShowCursor`/`WM_SETCURSOR`/`ProtectedCursor` 都无法覆盖。新增 [CursorHider.cs](src/Wiina.App/Interop/CursorHider.cs)：解析 PE 导入表、`VirtualProtect` 后把候选模块 IAT 中的 `user32!SetCursor` 重定向到 `UnmanagedCallersOnly` detour（只改指针、不改代码，原指针保活），隐藏期间统一改为 `SetCursor(NULL)`。实测隐藏后 20/20 次 `GetCursorInfo` 为 `hCursor=0 flags=0` 并持续 4.75 s，移动后 54 次 `flags=1` 恢复。`WIINA_CURSOR_HOOK=0` 关闭、`=scan` 仅枚举；安装失败自动回退旧机制。风险：杀软可能对 IAT patch 告警，WinAppSDK 升级若改模块名也要重新适配。
- **全屏顶部 1–3 px 条**：WinUI XAML 岛子窗口 `Microsoft.UI.Content.DesktopChildSiteBridge` 默认位于 (0,1)；`WindowInterop.CoverClientArea` 在启动与 presenter 变化时把客户区铺满，实测第 0 行已是视频像素。
- **全屏下不再拖出全屏**：全屏时内容区拖动直接返回，`WM_NCHITTEST` 对标题栏区域返回 `HTCLIENT`，并临时解除标题栏拖拽；实测拖动 240 px 不会退出全屏。
- **OSC 按钮不再抢 Space/Enter**：媒体 chrome 递归设置 `IsTabStop=false` / `AllowFocusOnInteraction=false`，小窗与全屏按 Tab 会被收回 `RootGrid`，快捷键全程可用。
- **OSC 菜单整理**：固定 8 组（文件/播放/视频/音频/字幕/插件/窗口/帮助）；视频菜单只保留画面项，窗口控制统一到窗口菜单；截图字幕开关从展开菜单顶层移到 `⋯ → 视频`；菜单项 83 → 80，`MenuRulesTests` 13 项通过。
- **OSC 自动隐藏加固**：5×5 DIP 抖动阈值、悬停保活（指针停在 chrome 上不倒计时）、移出窗口/失焦立即隐藏、左键隐藏遵循「控制 → 鼠标 → 单击」、光标随 OSC 一起隐藏、快速移入移出不再让计时器错乱。
- **时间轴拖动与快捷键显示进度 OSD**：拖动时间轴、方向键与快进/快退都走同一 `ShowOsd(OsdMessages.Seek(...))` 路径。
- **丢帧提示移到画面左侧垂直居中**：瞬时丢帧提示用 ASS 标签定位到画面左侧中部（`\an5` + `\pos`，x ≈ 画面宽/40），不再与右上角统计面板重叠；统计面板内的丢帧行不变。
- **卸载器启动即 UAC**：Inno `InitializeUninstall` 在非管理员时用 `runas` 重启卸载器并退出旧实例，删除了原先的 PowerShell `runas` 子进程；不再弹「卸载不干净」对话框，HKLM 残留只写日志。静默卸载保持不提权，安装包本身仍按用户安装。
- **截图命名**：`视频名-时-分-秒-毫秒.png`（如 `movie-1-28-39-980.png`），同名自动递增编号，非法字符清洗；无媒体路径时回退 `wiina-yyyyMMdd-HHmmss.png`。
- **缩略图开关与缓存**：删除设置里的「缩略图缓存数量/下限」行；预览总开关关闭时 `ThumbnailService` 直接返回，不创建 headless mpv、不写磁盘；内存缓存默认覆盖当前视频全部 5 秒桶（约 57.6 KB/桶）。
- **关闭耗时与设备丢失**：K01 关闭路径 170–179 ms（进程退出 362–426 ms），单次 `SaveSettings`、无看门狗；K02 新增 [DeviceLoss.cs](src/Wiina.Core/Playback/DeviceLoss.cs) 分类 `0x887A0005/6/7/20` 并有界 `video-reload` 重建（未做真实 TDR 实机验证）。
- **已知问题决策**：30 条清单已按用户决策标注 feature / 预期行为 / 已修，见 [known-issues-1.3.1](docs/reports/known-issues-1.3.1.md)。
- 测试 827 项通过；产物 `wiina-1.3.2-win-x64.zip` / `Wiina-Setup-1.3.2-x64.exe`（大小与 SHA-256 以验收页构建后记录为准）。
- 文档同步：[v1.3.2 验收](docs/acceptance/v1.3.2.md)。

### v1.3.1：快捷键配置收敛、OSC 菜单整理与缩略图 GPU 选项

- **快捷键只保留 Wiina 与 mpv**：移除近似复刻的 IINA 预设（旧配置中的 `IINA` 自动迁到 Wiina），Wiina 默认键位来自 `wiina-defaults.conf`，新增 `Ctrl+,` 打开设置；mpv 配置名称去掉“默认”后缀。自定义配置不受影响，见 [IINA 快捷键参考](docs/reference/iina-shortcuts.md)。
- **OSC 菜单去重 + 动态快捷键提示**：视频/窗口的重复项只保留窗口版本（把窗口缺少的截图/去色带/滤镜/画质移过去），菜单文本按当前配置实际键位显示。
- **速度档位只放在 OSC 展开菜单**：`⋯ → 置顶 → 速度 ▸` 提供 0.5/0.75/1/1.25/1.5/2/2.5/3/4×（按后续澄清，快速菜单首页不再放）；播放页滑杆保留，速度文字只在右侧显示。
- **上一首/下一首可改 ±5 秒**：设置 → 界面 → 窗口与 OSC（`ui.osc-prev-next`），按住 Ctrl 为 ±30 秒。
- **指针移出立即隐藏 OSC**：`PointerExited` 确认光标真的离开窗口后直接隐藏，不再等自动隐藏延时；flyout 未关、左键拖动或侧栏可见时仍不隐藏（先记下，等弹出层关闭再立即隐藏）。
- **丢帧提示移到左中**：瞬时 `Wiina · 丢帧 N 帧/分` 通过 ASS `\an4` 定位到画面左侧中部，不再遮挡右上角统计面板。
- **缩略图可用 GPU 生成**：设置 → 视频 → 缩略图新增「缩略图加速：自动（推荐）/仅 CPU」。自动档沿用 libmpv `hwdec=auto-safe`，仅 CPU 档 `hwdec=no`；实测自动 29 ms wall / 15.6 ms CPU 每帧，仅 CPU 104 ms / 461 ms。
- **缩略图内存缓存覆盖全片**：当前视频所有 5 秒桶都留在内存，约 57.6 KB/桶（≈41 MB/小时；24 分钟约 16 MB，2 小时约 83 MB），磁盘预算仍可调。
- **截图可含字幕**：设置 → 视频 → 截图 与 OSC 展开菜单两处同一开关（默认开），截图时以 `subtitles`/`video` 标志传给 mpv。
- **卸载器始终提权**：卸载器 manifest 为 `requireAdministrator`，始终请求管理员；检测到 Wiina 运行中会提示是否强制结束；卸载始终清理 HKCU+HKLM 关联，只问是否保留配置。安装包本身仍按用户安装，不需要管理员。
- **光标隐藏（部分实现）**：透明光标资源 101、`RootGrid` 下 489 个元素 `ProtectedCursor`、60 ms 辅助线程 `SetCursor(NULL)` 均已接入并通过构建/测试，但**未能稳定隐藏**（CoreMessaging 毫秒级贴回），按“已实现、未实机确认”记录，不写作已修复，详见 [已知问题清单](docs/reports/known-issues-1.3.1.md)。
- **已知问题汇总**：新增 [known-issues-1.3.1](docs/reports/known-issues-1.3.1.md)，30 条（P0/P1/P2）含根因、绕法、修法成本与勾选框，供决定下一轮范围。
- 测试 780 项通过；产物 `wiina-1.3.1-win-x64.zip` / `Wiina-Setup-1.3.1-x64.exe`（大小与 SHA-256 以验收页构建后记录为准）。
- 文档同步：[v1.3.1 验收](docs/acceptance/v1.3.1.md)。

### v1.3.0：设置重排、快捷键编辑器重建与 Windows 集成

- **设置精简为 11 页**：缩略图相关 8 项集中到「视频 → 缩略图」（预览、宽度、时间桶、缓存数量、磁盘上限、慢速模式、占用提示、清理）；原「工具」页并入「高级」（字体设置移除，日志查看器/检查器/关于归入高级页工具分节）；「打开 URL」移到通用页，「截图目录」移到视频页。行数 125 → **126**，本地化键 551 → **562**。完整快照见[设置界面快照](docs/reference/settings-layout.md)。
- **设置搜索更好用**：搜索框下新增提示行，说明 `Ctrl+Shift+F` 聚焦搜索、`Esc` 清空，以及搜索范围（页名/标题/说明/关键字）；搜索命中仍按行定位并滚动到该行。
- **数字输入框修复**：数字框宽度 150、最小 120，上下微调按钮改为紧凑模式（收进弹出层），点击后数字不再被挤成空白，焦点框与文本对齐；键盘逐位输入、退格、空值与越界夹取均正常。
- **切换页面滚动位置归零**：侧栏切换页签、设置窗口换页后都回到顶部（递归重置内部 `ScrollViewer` 并使用 `ChangeView(..., disableAnimation)`），不再停在上一页的滚动位置。
- **菜单显示真实快捷键**：菜单项根据当前快捷键配置（`KeyBindings.PrimaryChords` + 当前配置）计算并显示**实际生效**的键位，而不是编译期默认值；改键后菜单立即一致。
- **速度档位与动画收敛**：侧栏「布局」页新增速度档位下拉（0.5×–4×），与速度滑杆双向同步；移除「非必要动画」开关，动画统一跟随系统「动画效果」设置与设置窗口的界面动画开关。
- **缩略图慢速模式**：新增「慢速预览图模式（单核）」（`video.thumbnail-slow`）：单线程、低优先级、逐张生成、全片不做预生成，低配设备更不容易卡顿，代价是首次悬停稍慢；与 1.2.7 的磁盘缓存/预生成策略（`ThumbnailPolicy`）配合生效。
- **Windows 音量与媒体会话**：注册系统媒体传输控制（SMTC）会话，Windows 音量混音器显示 Wiina，媒体键/系统 OSD 可用；新增「设置 → 控制 → 系统媒体键」开关（`control.media-keys`，默认开，切换立即生效）。
- **全屏无边框**：窗口圆角/边框统一走 `WindowInterop.WindowFrameStyle`（Default / Snapped / Fullscreen）；全屏时 `DWMWCP_DONOTROUND` + 无边框色，退出全屏恢复默认，吸附态单独一档，Windows 10 为 no-op。
- **安装/升级行为**：安装包改为就地升级（`CloseApplications=yes`、`RestartApplications=no`），不再强制结束 Wiina 或删除旧配置；卸载提示「是否保留配置文件？」默认保留，静默卸载同样保留。
- **崩溃日志路径**：崩溃对话框的按钮改为实际执行（打开日志文件夹、复制路径），不再声称“收集日志”却没有产物；日志位于 `%LOCALAPPDATA%\Wiina\logs`。
- **快捷键编辑器重建**：可搜索、按分类分组的动作列表；Wiina 默认 / mpv 默认 / IINA 三套内置配置只读（1.3.1 起移除 IINA，见下），自定义配置可增删改；录制时做冲突检测（Wiina 的 `FindConflict` 是超出 IINA 的能力，保留）、未保存修改在关闭时拦截、支持 JSON 导入导出与按配置恢复默认。IINA 预设是**近似复刻**，与 IINA 真实默认有 6 处差异（见 [IINA 快捷键参考](docs/reference/iina-shortcuts.md)）。
- **mpv.net 导出（可选实验）**：`resources/mpvnet/` 提供 `wiina.conf` 与 `wiina-input.conf`。注意 Windows 上 mpv.net 的 `mpv.conf` **只接受选项**，快捷键必须写在 `input.conf`；便携版配置目录是 `<mpvnet.exe>\config\`，安装版是 `%APPDATA%\mpv.net\`。Wiina 侧栏、OSD 与合成相关选项在 mpv.net 中不可用，详见 [mpv.net 导出](resources/mpvnet/README.md)。
- **仍未闭环**：OSC 偶发冻结的根因（退出竞态、GPU 设备丢失后无恢复、8 Hz 全帧回读）记录在 [冻结分析](docs/reports/ui-freeze-analysis.md)，本版只落地了低风险项，R1/R2 与回读降频需后续版本。
- 测试 761 项通过；产物 `wiina-1.3.0-win-x64.zip` / `Wiina-Setup-1.3.0-x64.exe`（大小与 SHA-256 以验收页构建后记录为准）。
- 文档同步：[v1.3.0 验收](docs/acceptance/v1.3.0.md)。
### v1.2.7：缩略图缓存、管理员关联与关闭稳定性

- **时间轴缩略图对齐 IINA**：文件加载完成后在后台**一次性预生成**整条时间轴的缩略图（每 5 秒一格、90 px 高），
  写入 `%LOCALAPPDATA%\Wiina\thumbnails\v2`（每格一张 JPEG + 原子重写的 `index.json`，按字节 LRU 清理，
  默认预算 512 MB）。悬停时先查磁盘/内存缓存，未命中再由同一个提取 worker 插队取帧；再次打开同一文件基本即时显示。
  设置 → 界面 → 缩略图新增「缩略图磁盘缓存」（0–4096 MB，0 = 关闭）与「清除缩略图缓存」。
  网络/UNC 源只走内存缓存，不落盘。实测 1080p 23:40 文件：冷启动预生成 285 格 14.1–14.9 s，热缓存 0.3 s；
  悬停冷 73 ms、热 4 ms，285 格共 1.70 MB。
- **收起侧栏时浮动 OSC 跟手**：侧栏滑出动画期间不再先被夹住再回弹——正在隐藏的侧栏视为 0 让位
  （`SidebarHost.IsHiding`），隐藏动画开始时立即重算 OSC 位置。
- **光标隐藏（1.3.0 受限；1.3.1 仍为部分实现，未实机确认）**：WinUI 3 桌面窗口的光标由 CoreMessaging 驱动，`WM_SETCURSOR`、线程级
  `SetCursor` 与 `UIElement.ProtectedCursor=null` 都不会真正隐藏指针（根因与后续方案见 known-issues）。
  本轮已完整定位并实现三种尝试，但尚未生效；v1.3.1 已按此改用内嵌透明 `.cur` 资源 + `InputDesktopResourceCursor`，仍待实机确认。（1.3.2 已修复：进程内 IAT hook，见上方 v1.3.2 小节）
- **0.5× / 1× / 2× 使用真实视频分辨率**：新增 `WindowSizing.ScaledVideoWindowSize`，按 mpv 的显示尺寸
  （含旋转/像素比）换算物理像素后得到窗口的 DIP 尺寸，保持显示宽高比并夹到工作区；无视频时回退 1280×720，
  「适应屏幕」同样按视频比例选择最大窗口。
- **全屏不再有细边框**：圆角与边框色统一由 `WindowInterop.SetWindowFrame` 控制，全屏优先于吸附；
  切换全屏/presenter 后重新应用。Windows 10 上相关 DWM 属性被忽略（无副作用）。
- **文件关联支持管理员模式**：设置 → 文件关联新增「管理员一键关联…」，经 UAC 提权后写
  `HKLM\Software\Classes`（ProgID、Capabilities、RegisteredApplications），并尽力清除对应扩展名的
  `UserChoice`；取消关联会同时清理 HKCU 与 HKLM 中 Wiina 自己的项。按用户（HKCU）模式保留；
  Windows 仍要求在“默认应用”中最终确认。
- **未签名下载说明**：README 顶部新增 SmartScreen / 杀毒误报的继续方法与 SHA-256 校验步骤。
- **关闭路径与卡死分析**：关闭时先停所有 UI 定时器、设置只保存一次、先解绑视频面板再释放 mpv；
  统计面板隐藏时不再刷新。完整分析见 [卡死分析报告](docs/reports/hang-analysis-1.2.7.md)
  （报告中列出的 `ChromeBlur` 停止/事件解绑等项尚未落地，结果见验收页的如实记录）。
- **性能对照**：画质预设 23 项均实时生效，只有 `vo` / `gpu-api` / `gpu-context` / `d3d11-output-mode`
  在启动期固定；与 PotPlayer 的差距主要来自 composition swapchain、8 Hz 毛玻璃回读与常驻 headless mpv，
  见 [性能对照](docs/reports/performance-vs-potplayer.md)。
- 测试 711 项通过（新增缩略图磁盘缓存与缩放尺寸测试）；产物 `wiina-1.2.7-win-x64.zip` /
  `Wiina-Setup-1.2.7-x64.exe`（大小与 SHA-256 以验收页构建后记录为准）。
- 文档同步：[v1.2.7 验收](docs/acceptance/v1.2.7.md)。

### v1.2.6：OSC 避让、自动隐藏计时与音频输出设备

- **OSC 只在确实被遮挡时让位**：打开快速设置/播放列表/章节/插件侧栏时，浮动 OSC 不再无条件平移；
  只有侧栏面板与 OSC 在垂直方向实际重叠时，才横向贴边让开（保留 8 DIP 间距）。
  拖动浮动 OSC、恢复保存位置也走同一规则。Docked 布局的底栏属于布局分栏，仍随侧栏让位。
- **自动隐藏由明确的空闲时钟驱动**：新增「设置 → 界面 → 窗口与 OSC → 自动隐藏延时」
  （0.5–30 秒，默认 2.5，行 id `ui.osc-autohide-seconds`）。指针活动记在单调 `Stopwatch` 上；
  单轴小于 5 DIP 的抖动既不算活动、也不重置计时，所以手搭在鼠标上不会让控制条一直亮着。
  全屏下鼠标停在底部控制条上同样会按时淡出；失去焦点（非 Docked 且无侧栏）立即隐藏；
  点击画面隐藏要求按下到抬起没有明显位移；Docked 布局从不自动隐藏，PiP/迷你模式不受影响。
- **音频输出设备改为下拉选择**：设置 → 音频 → 音量与输出从 libmpv 的 `audio-device-list` 读取，
  首项为「跟随系统默认」，带刷新按钮；设备重名时用描述区分，悬停显示原始设备名。
  已保存但 mpv 当前未报告的设备保留为「不可用（…）」占位，不会被静默改写；切换后立即生效；
  播放器尚未创建时只提供系统默认项。
- **设置仍为 12 页**，新增自动隐藏延时一行后共 **122 行**，既有行 id 未变。
- **新增参考文档**：[设置界面快照](docs/reference/settings-layout.md)（12 页 / 122 行，供重排对照）、
  [IINA 缩略图调研](docs/reference/iina-thumbnails.md)（IINA 预生成 + 磁盘缓存 vs Wiina 悬停实时抽帧；
  1.2.7 起已实现预生成 + 磁盘缓存）。
- 测试 668 项通过；产物 `wiina-1.2.6-win-x64.zip` / `Wiina-Setup-1.2.6-x64.exe`。
- 文档同步：[v1.2.6 验收](docs/acceptance/v1.2.6.md)。

### v1.2.5：主题同步、纯色档修复与画质管理

- **主题即时同步到所有窗口**：在设置、快速菜单或任一窗口切换深/浅色，播放窗口（标题栏 + 浮动 OSC）、
  侧栏、设置、快速设置、PiP、关于、日志、检查器、滤镜、键位、欢迎与画质窗口都会立即重绘。
  `ThemeMode.System` 现在实时跟随 Windows 应用主题（WinUI 的 `ElementTheme.Default` 不会），
  两个方向都生效；主题行在两处都会显示新值。限制：窗口材质（Mica/亚克力）可能晚一帧，
  高对比度下的深浅切换未做视觉验证。
- **纯色档真正不透明**：从毛玻璃切到「关闭毛玻璃（纯色）」不再透出视频。外框面板不再由代码写
  `Background`，模糊层/tint 层是子元素、只切换可见性；深色基色改为深灰 `#202227`（不再是近黑），
  深色 tint 同步对齐。四档可热切换，来回切无需隐藏/显示。
- **OSD 也接入毛玻璃**：OSD 卡片跟随全局四档（固定/动态/始终为玻璃，纯色为不透明），
  仍不拦截鼠标输入。
- **画质管理扩展到 23 项 mpv 选项**：按 缩放 / 色度 / 抖动 / 去色带 / 插帧 / 同步 / 其他 分组，
  每项有中文标签、取舍说明与原始 mpv 键；默认档对齐 mpv 0.41（IINA 不覆盖）的默认值：
  `scale=lanczos`、`dscale=hermite`、`cscale=bilinear`、`dither=fruit`、`dither-size-fruit=6`
  等；「性能优先」= bilinear ×3、`dither=no`、质量开关全关、去色带关。去色带总开关移到
  设置 → 视频 → 画面（默认关），profile 参数只在开启时下发给 mpv；未知导入键保留为
  “未识别（保留原值）”。修复 9 个数字输入框丢失 `Grid.Column` 导致微调按钮压住标签的问题。
- 测试 659 项通过；产物 `wiina-1.2.5-win-x64.zip` / `Wiina-Setup-1.2.5-x64.exe`。
- 文档同步：[v1.2.5 验收](docs/acceptance/v1.2.5.md)、[ADR-0003](docs/architecture/0003-acrylic.md)
  更新 OSD 表面，[ADR-0004](docs/architecture/0004-solid-chrome.md) 仅描述纯色档。

### v1.2.4：毛玻璃回归、贴边吸附与安装包

- **毛玻璃回归为四个可选档位**：设置 → 界面 → 窗口与 OSC 的「毛玻璃模式」提供
  固定（静态帧）/ 动态（跟随视频，默认）/ 始终（持续计算）/ 关闭毛玻璃（纯色）。
  前三档由 `Interop/ChromeBlur.cs` 每 125 ms 从 libmpv 取一帧，320 px 降采样后用
  Win2D 高斯模糊（半径 9）分发到标题栏、底部/浮动 OSC 与侧边栏；OSD 消息卡片自 1.2.5 起跟随同一档位（三档玻璃、纯色不透明）。
- **纯色档零取帧开销**：不创建 Win2D 设备、不启动取帧线程，只画不透明
  `ChromeSolidBrush` 并混入 16% 系统 accent（`Services/ChromeTint.cs`），切换 accent
  立即生效；`WIINA_BLUR_DISABLE=1` 可强制纯色。
- **小窗全分辨率**：移除 640×360 合成器放大上限，`PipGeometry.RenderSize` 直接返回
  整窗物理像素，小窗只靠简化 UI（无毛玻璃/阴影）省开销，画面与主窗口同样清晰。
- **吸附真正贴边**：新增 `Core/State/FrameMargins.cs` 与 `WindowInterop.TryGetFrameMargins`，
  按 DWM 可见边框（`DWMWA_EXTENDED_FRAME_BOUNDS`）换算后再吸附；10 DIP 吸附 /
  22 DIP 脱离迟滞，按显示器 DPI 缩放。标题栏、视频区域、迷你播放器与 PiP 四条拖动路径
  都生效，四条边都可贴平；吸附期间去掉 DWM 边框线与圆角。
- **统计面板跟随 OSD**：统计卡片按 OSD 位置摆放，深色主题使用主题文字色；持久技术
  OSD 功能整体移除，统计面板是唯一统计界面，仍为 1 秒刷新。
- **画质预设精简为两档**：只保留 默认（IINA/libmpv 缩放组合）与 性能优先（全部
  Bilinear）；旧「均衡 / 高质量」在导入 JSON 时归一化为默认；去色带移到
  设置 → 视频 → 画面，默认关闭并即时应用到 mpv。
- **设置侧栏**：未选中项的悬停与静止完全一致（暗色，无填充/边框），选中项保留染色，
  键盘焦点为 2 px accent 环。
- **安装包与卸载**：新增按用户（无需管理员）的 Inno Setup 安装包
  `Wiina-Setup-1.2.4-x64.exe`；卸载时询问「是否保留配置文件？」，默认保留，选“否”
  才删除 `%LOCALAPPDATA%\Wiina` 与 Wiina 自己的 HKCU 关联项，永不触碰媒体文件。
  便携 zip 也附带 `Uninstall Wiina.exe`。
- 文档同步：[v1.2.4 验收](docs/acceptance/v1.2.4.md)、[安装与卸载](docs/installer.md)、
  [ADR-0003](docs/architecture/0003-acrylic.md) 恢复为当前设计。

### v1.2.3：文档、稳定性与文件关联

- **OSC 快速设置**：从播放列表 / 章节 / 插件页点击任意快速设置页签，都会切回设置页并展开对应页，
  不再出现“页签点不到”。
- **窗口外框改为不透明纯色**（1.2.4 起恢复为可选毛玻璃，纯色只是四个档位之一）：移除逐帧取帧 +
  Win2D 高斯模糊管线，标题栏、底部/浮动 OSC、侧边栏与 OSD 统一使用不透明面板色，并按系统 accent
  以 16% 权重染色；无透出、无逐帧 GPU 回读。
- **播放统计面板**：由 mpv `show-text` 单行 OSD 改为原生 WinUI 统计卡片，1 秒刷新，打开设置或启动后
  立即填充，按 文件 / 视频 / 音频 / 播放 / 渲染 分组，右上角按钮关闭并记住选择。
- **小窗缩放与吸附**（1.2.4 起取消 640×360 上限并改用 DWM 可见边框吸附）：PiP 画面布局与 640×360 渲染上限解耦，放大后内容随之缩放（放大只改变合成器采样源，
  不再缩水 XAML 面板）；边缘吸附改为共享 `EdgeSnapSession`，吸附距离 10 DIP、脱离迟滞 22 DIP，
  紧贴显示器工作区边缘（吸附时去掉 DWM 边框线与圆角），主窗口任意区域与 PiP 拖动路径都生效，
  避免“吸上拿不下来”。
- **设置侧栏**：未选中的导航项不再在悬停/按下时变亮，保持暗色；仅选中项保留高亮，键盘焦点框仍然可见。
- **画质预设**（1.2.4 起精简为默认/性能优先两档）：管理窗口改用可读标签说明每个选项的取舍与推荐角色，不再直接罗列裸 mpv 名称。
- **OSC 自动隐藏加固**：弹出菜单计数、指针释放重新计时、左键按下守卫、透明度感知命中测试、
  可取消的单一外框动画，统计面板也计入“外框可见”，修复偶发“发疯”。
- **文件关联**：设置新增第 12 页「文件关联」，可按当前用户（HKCU）注册常见视频/音频格式，
  取消关联只删除 Wiina 自己的 ProgID 与 OpenWith 项；Windows 仍要求在“默认应用”中确认，不静默抢占。
- **WinUI 3 适配**：标题栏自定义 API 增加能力检查（Windows 10 安全降级）、PiP 主题/计时器清理、
  统计面板关闭状态持久化、显示器回退更诚实、切换语言时关闭画质预设窗口。
- 文档同步：README、ADR-0003 归档与 ADR-0004、文件关联说明、性能/无障碍/许可证审计与
  [v1.2.3 验收](docs/acceptance/v1.2.3.md)。

### 后续修复：fix/v1-polish

- 无媒体启动只显示**欢迎窗口**；打开媒体后创建播放器并关闭欢迎。
- 设置窗口默认 **760×600 DIP**，记住大小/位置。
- 标题栏/OSC/设置使用**亚克力**（当时；1.2.3 曾改为不透明纯色，1.2.4 起恢复为可选毛玻璃）；主播放器使用 WinUI 自定义窗口按钮，去掉 DWM 的纯色 caption 块。
- **IINA 智能字幕**：同名字幕自动发现 + 语言优先；**外挂音频**：同名 `.mka` 自动加载 + 手动选择器。
- **IINA 快捷键方案**：Default / IINA / Custom，可在 Window 菜单切换并持久化。
- 详细记录：[docs/acceptance/v1-polish.md](docs/acceptance/v1-polish.md)。

### 后续修复 2：fix/v1-polish-2

- 锁宽高比支持**所有窗口边缘**；时间/章节预览按 timeline 实际位置锚定并 clamp。
- OSC 音量条上限 100%，键盘可超过（上限在设置-Audio）；OSD 位置扩展为六向。
- 首次播放按视频分辨率设置窗口（HiDPI 用物理像素；超屏用半尺寸）。
- 设置底部 footer 移除；键位编辑独立到**亚克力 KeyBindingWindow**（控制 → 键盘方案 → 编辑键位）。
- 欢迎窗口可缩放并记忆大小/位置；白底黄三角图标；`scripts/release.ps1` 版本同步发布脚本。
- 详细记录：[docs/acceptance/v1-polish2.md](docs/acceptance/v1-polish2.md)。

### 后续修复 3：fix/v1-polish-3

- **真视频背景模糊**（1.2.4 起回归为可选四档；1.2.3 曾改为不透明纯色）：`Interop/ChromeBlur.cs` 每 125 ms 从 libmpv 取当前帧，320 px 降采样后
  用 Win2D 高斯模糊，再按控件屏幕位置裁切分发给标题栏、OSC、侧边栏与 OSD；不再用平染色冒充。
- 时间/章节预览改为按**指针实际 X** 锚定，不再随进度比例漂移。
- 欢迎窗口移除底部拖放提示，图标改为白底黄色正三角；音量条移除 100% 竖线缺口；OSD 进度条
  改为固定 8 px 圆角横条（1.0.2 进一步按 100% 缩放收为 4 px）。
- PiP 主窗口占位只保留一个“返回”按钮；进入 PiP 自动最小化主窗口，恢复/关闭 PiP 时还原。
- 详细记录：[docs/acceptance/v1-polish3.md](docs/acceptance/v1-polish3.md)。

### 后续修复 4：fix/v1-polish-3 / v1.0.2

- 浮层 OSC 在真实视频模糊接管后恢复**拖动**：模糊首帧不再把 `DragArea.Background`
  置空，改为透明画刷保留命中测试。
- 时间/章节预览的卡片**测量前先清零 Margin**，用干净的 `DesiredSize` 计算宽度，并按指针
  实际 X 锚定；修复 1.0.1 中残留的横向漂移。
- OSD 横向进度条在 100% 缩放下为 **4 px 高、2 px 圆角**。
- 打包强制 `Platform=x64` 并直接校验待打包 `Wiina.dll` 的版本，避免把旧版本装进新包。
- 详细记录：[docs/acceptance/v1-polish4.md](docs/acceptance/v1-polish4.md)。

### v1.1.2：原生音量条、单层导航与操作模式

- 音量条改回**原生 WinUI 3 Slider**：85 DIP 轨道、左右 8 DIP 内边距，滑块使用系统 Fluent 圆点，
  0% / 100% 两端不再露出横线；所有重点色改用系统 accent（不再写死蓝色）。
- OSC 的**全屏与播放列表按钮交换**位置。
- 快速设置改为**单层定位导航**：只有左上角关闭按钮会关闭面板；类别行移到顶部中央，
  使用图标 + 文字 + 底部 accent 高亮。打开快速设置时类别行直接展开为
  布局 / 视频 / 音频 / 字幕 / 播放列表 / 章节 / 插件，点击只切换页面、不会关闭侧栏；
  面板打开期间标题栏与 OSC 保持显示（操作模式）。
- 详细记录：[docs/acceptance/v1.1.2.md](docs/acceptance/v1.1.2.md)。

### v1.1.0：浅色主题、下划线页签与 HiDPI

- 修复偶发 `F11` 提示浮窗（移除全屏按钮 tooltip）；浅色模式下 OSD 使用浅色背景/深色文字。
- 快速设置的侧边栏类型页签与页面页签统一为 **WinUI 下划线页签**（accent 文字 + 2 px 下划线）。
- HiDPI：音量滑杆左右留出滑块空间，浮动/底部 OSC 改用 `MinWidth/MinHeight`，避免最大值圆点与文字被裁切。
- 新增 **自动添加播放列表**：同目录文件（默认）/仅手动/同目录及子文件夹。
- 关于界面使用黄色三角品牌图标并透出 acrylic；设置窗口去掉左上角图标、搜索框移入标题栏，左右两栏亮度一致。
- 标题栏最小化/最大化/关闭按钮 hover/pressed 背景覆盖完整 40 DIP 行。
- OSC 展开菜单顶部新增“置顶/取消置顶”。
- 详细记录：[docs/acceptance/v1.1.0.md](docs/acceptance/v1.1.0.md)。

### v1.1.1：设置、页签与 HiDPI 修复

- F11 浮窗彻底移除：删掉 XAML 中重复的 F11 accelerator，只保留可配置按键映射。
- OSD 跟随设置中的深浅色即时切换，不再需要重启；模糊层注入的 OSD 底色也会更新。
- 快速设置页签改为**原生圆角方框**选中态；搜索框居中；设置移入 OSC 更多菜单底部。
- 已打开的设置/快捷键窗口再次打开会恢复到前台。
- 音量条重写为自绘 4 px 轨道 + 16 px 圆点，100%/200% 下不裁切；OSC 高度改 Min。
- 置顶时标题栏文件名前显示图钉；标题栏 caption 按钮 hover 覆盖完整 40 DIP。
- 详细记录：[docs/acceptance/v1.1.1.md](docs/acceptance/v1.1.1.md)。

### v1.2.0：窗口手感、智能字幕与画质分级

- PiP 的拖动条、双击提示和播放条仅在鼠标移入时显示；关闭按钮保留，返回按钮改为**置顶切换**，
  播放/暂停图标随真实状态同步。
- 拖动窗口靠近屏幕边缘时自动吸附；设置中可关闭该行为。
- 智能字幕升级为 IINA 风格的**宽松匹配**：忽略 1080p/x265 等发布标签，仍避免跨集误匹配。
- 视频设置新增 **mpv 画质预设**（1.2.4 起只保留 默认 / 性能优先；原“均衡 / 高质量”已删除），OSC 更多菜单可直接切换。
- 毛玻璃新增**性能模式**：固定帧 / 动态（默认）/ 始终计算；动态模式只在 titlebar/OSC 可见时采集。
- 设置页面切换加入轻微上滑淡入动画；快捷键编辑器使用 WinUI 键位胶囊显示。
- 详细记录：[docs/acceptance/v1.2.0.md](docs/acceptance/v1.2.0.md)。

### v1.2.1：快速设置、吸附与小窗打磨

- 快速设置改为单层定位导航：从播放列表打开时只显示 4 个类别；切到设置后展开为
  7 个页面页签（布局 / 视频 / 音频 / 字幕 / 播放列表 / 章节 / 插件），只有当前页显示文字，
  图标放大并收紧间距；切换页面后旧下划线会正确清除。
- 小窗控件只在窗口被选中且鼠标悬停时出现；OSC 菜单在鼠标离开窗口时可靠关闭。
- 窗口吸附改为拖动过程中实时生效，贴边不留空隙；设置中仍可关闭。
- 快捷键编辑器支持新建 / 重命名 / 删除 profile；键位胶囊使用系统 accent 色，面板背景不再过透。
- 视频画质预设新增管理窗口：可新建、重命名、删除 profile，导入 / 导出 JSON，并立即应用。
- 毛玻璃新增**无采集纯色模式**：完全关闭模糊管线，以带系统 accent 染色的纯色背景替代，
  面向低性能设备；动态模式在控件隐藏时停止采集。
- 快速设置页面切换加入与设置窗口一致的上滑淡入动画。
- 设置标题栏搜索框右侧可拖动窗口；滤镜 / 均衡器窗口改用 WinUI 自定义标题栏与半透明背景。
- 详细记录：[docs/acceptance/v1.2.1.md](docs/acceptance/v1.2.1.md)。

### v1.2.2：快速设置、小窗与性能收尾

- 快速设置固定显示 7 个页面图标，仅当前页显示文字；图标加大、间距收紧，切换页面时旧下划线可靠清除。
- 小窗拖动条/提示/控件仅在窗口被选中且鼠标悬停时出现，隐藏时不再跑自动隐藏计时器。
- 窗口吸附阈值收紧到 10 DIP 并在 `WM_MOVING` 拖动过程中实时贴边，不留可见空隙。
- OSC 菜单在窗口失焦、鼠标离开或 OSC 隐藏时主动关闭，避免菜单残留。
- 快捷键方案移除“自定义”假 profile；首次编辑会自动创建可重命名 / 删除的命名方案，允许多个用户 profile。
- 画质预设新增管理窗口：新建 / 重命名 / 删除 / 导入导出 JSON 并立即应用；设置里的操作按钮显示“打开”。
- 新增 **mpv 统计信息** 设置与快速设置开关；当时通过 mpv OSD 文本显示帧率/丢帧（1.2.3 起改为原生统计面板）。
- 设置页与快速设置页切换动画保持一致；滤镜 / 均衡器与快捷键编辑器改用 WinUI 自定义标题栏/主题背景。
- 详细记录：[docs/acceptance/v1.2.2.md](docs/acceptance/v1.2.2.md)。

## 关键事实

| 项目 | 决定 |
|---|---|
| UI | WinUI 3（Windows App SDK 2.x），非 MSIX（unpackaged） |
| 播放内核 | libmpv（要求 mpv **0.41+**，固定构建见 `third_party/libmpv/VERSION.txt`） |
| 视频呈现 | mpv `--d3d11-output-mode=composition` 的 swapchain 绑定到 XAML `SwapChainPanel`，XAML UI 覆盖其上 |
| 窗口外框 | 可选实时毛玻璃（固定/动态/始终，`Interop/ChromeBlur.cs`，含 OSD 卡片）或不透明纯色（`ChromeSolidBrush` + `Services/ChromeTint.cs`，16% 系统 accent，深色基色 `#202227`）；默认动态 |
| 分发 | 便携 zip + 按用户 Inno Setup 安装包，两者都带卸载入口（可保留配置） |
| 语言/运行时 | C# / .NET 10，self-contained win-x64 |
| 界面语言 | 简体中文（默认）与 English，可在设置中即时切换 |
| 许可证 | **GPL-3.0-only**（见 [LICENSE](LICENSE)、[NOTICE](NOTICE)、[许可证审计](docs/licensing/AUDIT.md)） |
| 上游基线 | IINA `develop` @ `5c15f81568b1f61f85219302843018e3c43c8d3b`（2026-10-16） |

## 安装与运行

发布包有两种形式，都不需要单独安装 .NET 或 Windows App Runtime：

1. **便携 zip**：获取 `artifacts/wiina-1.3.2-win-x64.zip`，解压到任意可写目录，运行 `Wiina.exe`；
   卸载时运行同目录的 `Uninstall Wiina.exe`（可保留配置）。
2. **安装包**：获取 `artifacts/Wiina-Setup-1.3.2-x64.exe`，按当前用户安装（默认
   `%LOCALAPPDATA%\Programs\Wiina`，无需管理员），从“设置 → 应用”卸载；卸载器启动时请求
   管理员（清理 HKLM 关联），随后只询问「是否保留配置文件？」，默认保留。

设置、日志与插件目录位于 `%LOCALAPPDATA%\Wiina`。首次启动可拖入文件/文件夹/URL，或使用欢迎窗口打开媒体。
安装包、卸载行为与静默参数见 [docs/installer.md](docs/installer.md)。

文件关联可在设置 → 文件关联按当前用户（HKCU）或管理员（HKLM，需 UAC）注册；两种方式都需要在 Windows「默认应用」中最终确认，详见 [docs/file-associations.md](docs/file-associations.md)。

## 构建

前置：

- Windows 11 22H2+（Windows 10 2004+ 为尽力支持）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- mpv 0.41+ 的 `libmpv-2.dll`：运行 `tools/fetch-libmpv.ps1`（下载到 `third_party/libmpv/`，该目录不入库）
- 可选：Visual Studio 2026（C# 桌面开发）+ Windows App SDK
- 可选：Inno Setup 6（仅安装包需要；`ISCC.exe` 在 PATH 或 `%LOCALAPPDATA%\Programs\Inno Setup 6`）

```powershell
# 1) 取 libmpv
powershell -ExecutionPolicy Bypass -File tools/fetch-libmpv.ps1

# 2) 构建（self-contained：目标机不需要额外安装 .NET / Windows App Runtime）
# 直接构建 App 项目并指定 x64；对整个 .slnx 传 -p:Platform=x64 会因解决方案平台映射失败。
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\src\Wiina.App\Wiina.App.csproj -c Release -p:Platform=x64

# 3) 运行
.\src\Wiina.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Wiina.exe
```

> 若 `dotnet` 不在 PATH，可使用 `%USERPROFILE%\.dotnet\dotnet.exe`（用户级安装）。
> 发布构建为 `x64`；ARM64 未验证，不在 v1 范围内。

## 测试

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Release
```

测试覆盖设置/搜索/菜单规则、快捷键、OSC/OSD、播放列表/历史、滤镜/EQ、窗口布局/几何、边缘吸附、
文件关联注册计划，以及 Stage 11 的本地化目录完整性、语言解析/持久化取值、打包清单与无障碍元数据契约；
1.2.4 起还包括 DWM 可见边框换算（`FrameMarginsTests`）、画质预设归一化（`QualitySettingsTests`）与
小窗渲染尺寸（`RenderSizeTests`）；1.2.5 扩展了画质 profile 的序列化/默认值测试；1.2.6 增加 OSC 可见性状态机测试（`OscVisibilityControllerTests`）。1.2.7 增加缩略图磁盘缓存（`ThumbnailDiskCacheTests`）与按视频尺寸缩放（`WindowSizingScaledSizeTests`）；1.3.0 增加快捷键配置测试（`KeyBindingProfileTests`/`WiinaInputDefaultsTests`）、缩略图策略（`ThumbnailPolicyTests`）、丢帧检测（`FrameDropDetectorTests`）与恢复窗口位置（`WindowSizingRestoreTests`）；1.3.1 增加缩略图缓存容量（`ThumbnailCacheSizingTests`）、快捷键配置迁移与菜单去重（`KeyBindingProfileTests`、`WiinaInputDefaultsTests`、`MenuRulesTests`）、截图字幕（`PlaybackSettingsTests`）等测试。1.3.2 增加光标隐藏（`CursorHider` 导入表钩子，需实机验证）、截图命名（`ScreenshotNamingTests`）与设备丢失分类（`DeviceLossTests`）测试。当前 827 项测试在 Release 下全部通过。

## 打包（可复现）

```powershell
# 便携 zip（先构建 src/Wiina.Uninstall，再打包并校验卸载器与 Wiina.dll 版本）
pwsh -File scripts/package.ps1 -Version 1.3.2

# 安装包（在便携 zip 之后；需要 Inno Setup 6 的 ISCC.exe）
pwsh -File scripts/release.ps1 -Version 1.3.2
```

`package.ps1` 会：用 `dotnet build -c Release -r win-x64 --self-contained -p:Platform=x64 -t:Rebuild`
构建到 `src/Wiina.App/bin/x64/Release/...`（不使用 `dotnet publish`，避免 WinUI 3 unpackaged
应用出现 XBF parse 失败）→ 单独发布 `src/Wiina.Uninstall` 并把 `Uninstall Wiina.exe` 放进载荷 →
复制构建输出、`third_party/libmpv/libmpv-2.dll`、`LICENSE`、`NOTICE`、`README.md` 并生成
`SOURCE.txt`（源码地址 + 提交 + 构建时间）→ 校验必需文件与**待打包 `Wiina.dll` 的版本** →
生成 `artifacts/wiina-1.3.2-win-x64.zip`。

`release.ps1` 在便携 zip 之后解压同一份载荷到 `artifacts/publish`，用
`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`（或 PATH / Program Files 中的 ISCC）编译
`installer/wiina.iss`，输出 `artifacts/Wiina-Setup-1.3.2-x64.exe`，并打印两个产物的
大小与 SHA-256。`artifacts/` 与 `third_party/libmpv/` 均被 gitignore，仓库不提交二进制。
缺少任一必需文件、卸载器或版本不匹配时脚本直接失败。

## 本地化与无障碍

- 运行时字符串目录位于 `src/Wiina.Core/Localization/UiStringCatalog.cs`（zh-Hans/en 成对），
  设置目录的英文覆盖在 `SettingsLocalization.cs`，菜单在 `MenuLocalization.cs`；WinUI 3
  不支持自定义 markup extension，XAML 通过 `loc:Loc.Text` 等附加属性绑定并在切换语言时重放。
- 设置 → 通用 → 界面语言可即时切换；语言选择会持久化，重启后保持。主界面与设置窗口即时刷新，
  已打开的辅助窗口（欢迎/关于/日志/检查器/滤镜）在重新打开后使用新语言。
- 高对比度主题映射到系统颜色；`AnimationsEnabled` 与系统“显示动画”同时生效；播放器画面与 OSC
  固定为从左到右，避免 RTL 系统镜像进度/音量。详见 [docs/accessibility.md](docs/accessibility.md)。
- 深度技术字符串（检查器字段名、滤镜/编码器名称、调试/日志）保持原文，不机器翻译；
  覆盖范围与例外见 [docs/accessibility.md](docs/accessibility.md#7-localization-coverage)。

## 性能与已知问题

- 启动/内存实测方法与数字见 [docs/performance.md](docs/performance.md)。
- v1 已知问题与跳过范围见 [docs/release/known-issues.md](docs/release/known-issues.md)。
- 插件 API **未实现**（Stage 9 跳过），见 [docs/plugins/README.md](docs/plugins/README.md)。

## 文档

- [实施计划（历史，v1 前）](docs/PLAN.md) — 阶段、范围、验收标准；当前行为以本 README 与验收页为准
- [v1.3.2 验收](docs/acceptance/v1.3.2.md) — 光标隐藏、全屏修正、菜单/OSC 收尾与已知问题决策的验证清单
- [v1.3.1 验收](docs/acceptance/v1.3.1.md) — 快捷键配置收敛、OSC 菜单整理、缩略图 GPU 选项与卸载器提权的验证清单
- [v1.3.0 验收](docs/acceptance/v1.3.0.md) — 设置重排、快捷键编辑器重建、菜单修复与 Windows 集成的验证记录
- [v1.2.7 验收](docs/acceptance/v1.2.7.md) — 缩略图预生成/磁盘缓存、管理员关联、真实分辨率缩放与全屏边框的验证记录
- [v1.2.6 验收](docs/acceptance/v1.2.6.md) — OSC 避让/自动隐藏、音频设备下拉与参考文档的验证记录
- [v1.2.5 验收](docs/acceptance/v1.2.5.md) — 第三轮 4 项修复 + 画质选项清单的验证记录
- [v1.2.4 验收](docs/acceptance/v1.2.4.md) — 第二轮 10 项用户问题的验证清单
- [v1.2.3 验收](docs/acceptance/v1.2.3.md) — 第一轮 12 项用户问题的验证清单
- [Stage 11 验收](docs/acceptance/stage-11.md) — §19.2 清单与结果
- [无障碍与本地化说明](docs/accessibility.md)
- [性能测量](docs/performance.md)
- [设置界面快照](docs/reference/settings-layout.md) — 11 页 / 128 行的完整设置清单与窗口动作（1.3.2 快照）
- [已知问题决策清单](docs/reports/known-issues-1.3.1.md) — 30 条 P0/P1/P2，含根因、绕法与修法成本，可勾选下一轮范围
- [IINA 缩略图调研](docs/reference/iina-thumbnails.md) — 上游实现、Wiina 现状与 1.2.7 对齐实现；1.3.1 起可选 GPU 生成与全片内存缓存
- [IINA 快捷键参考](docs/reference/iina-shortcuts.md) — IINA 默认键位基线、编辑器 UX 与 profile 差异；1.3.1 起只保留 Wiina/mpv 两套
- [冻结与卡顿分析](docs/reports/ui-freeze-analysis.md) — 3–8 秒冻结的根因、成本图与优化优先级
- [与 PotPlayer 的性能对比](docs/reports/performance-vs-potplayer.md) — 画质选项是否真正生效、开销来源与优化清单
- [文件关联](docs/file-associations.md) — 按用户注册、默认应用确认与取消
- [安装与卸载](docs/installer.md) — Inno Setup 按用户安装、卸载器始终提权、保留配置与便携卸载器
- [发布说明](docs/release/notes.md) · [已知问题](docs/release/known-issues.md)
- [许可证审计](docs/licensing/AUDIT.md) — GPL/mpv/字体/图标/商标义务
- [ADR-0001 视频管线](docs/architecture/0001-video-pipeline.md) — 为什么用 composition swapchain
- [ADR-0003 视频帧毛玻璃](docs/architecture/0003-acrylic.md) — 取帧 + Win2D 管线与四个模式
- [ADR-0004 纯色模式](docs/architecture/0004-solid-chrome.md) — 关闭毛玻璃时的纯色/染色取舍
- [IINA macOS UI 复刻规格](docs/reference/IINA-macOS-UI-复刻规格.md) — 输入规格（GPL-3.0）

## 许可证与声明

- 本项目以 **GNU GPL v3.0 only** 发布。见 [LICENSE](LICENSE)。
- 对应源码：<https://github.com/RareSpecies233/wiina>。发布包内 `SOURCE.txt` 记录构建提交。
- 本项目**不是** IINA 或 mpv 的官方项目，与二者无隶属关系；“IINA” 名称与图标归其作者所有，本项目不使用。
- IINA 以 GPL-3.0 发布；本项目的界面行为参考其设计，Swift/AppKit 源码、图片资源与 SF Symbols 未被复制。
- mpv 默认以 GPL-2.0-or-later 发布；发布包动态链接未修改的 libmpv-2.dll，并在 [NOTICE](NOTICE)
  中提供许可证说明、源码获取方式与三年书面要约。
- SF Symbols 仅授权用于 Apple 平台；本项目改用 Segoe Fluent Icons / 自绘图标。
- 第三方组件与完整义务见 [NOTICE](NOTICE) 与 [许可证审计](docs/licensing/AUDIT.md)。