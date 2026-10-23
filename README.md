# Wiina

> Windows 11 原生、IINA 风格界面、mpv 内核的播放器。
> WinUI 3 · Windows App SDK · libmpv · C# / .NET 10

Wiina 把 [IINA](https://iina.io/) 在 macOS 上的**界面逻辑**（窗口层级、OSC、侧边栏、OSD、模式与状态机）带到 Windows，并用
[mpv](https://mpv.io/) 作为播放内核。视觉上不照搬 AppKit，而是按 Windows 11 Fluent 规范重新设计：Segoe UI Variable、
Segoe Fluent Icons、Mica/Acrylic、圆角与系统主题。

## 状态（v1.2.1）

- **Stage 0–8 已验收。** 播放管线、WinUI 标题栏、OSC、播放/轨道/字幕/章节/播放列表/历史、侧边栏与快速设置、
  OSD/缩略图、全屏/PiP/迷你模式/多窗口、11 页设置/搜索/快捷键/菜单/欢迎与辅助窗口、视频滤镜/10 段均衡器/
  音频滤镜/HDR 与高级音画均已实现。
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

### 后续修复：fix/v1-polish

- 无媒体启动只显示**欢迎窗口**；打开媒体后创建播放器并关闭欢迎。
- 设置窗口默认 **760×600 DIP**，记住大小/位置。
- 标题栏/OSC/设置使用**亚克力**；主播放器使用 WinUI 自定义窗口按钮，去掉 DWM 的纯色 caption 块。
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

- **真视频背景模糊**：`Interop/ChromeBlur.cs` 每 125 ms 从 libmpv 取当前帧，320 px 降采样后
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
- 视频设置新增 **mpv 画质预设**（自动 / 均衡 / 高质量 / 性能优先），OSC 更多菜单可直接切换。
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

### v1.1.2：原生音量条、单层导航与操作模式

- 音量条改回**原生 WinUI 3 Slider**：85 DIP 轨道、左右 8 DIP 内边距，滑块使用系统 Fluent 圆点，
  0% / 100% 两端不再露出横线；所有重点色改用系统 accent（不再写死蓝色）。
- OSC 的**全屏与播放列表按钮交换**位置。
- 快速设置改为**单层定位导航**：只有左上角关闭按钮会关闭面板；类别行移到顶部中央，
  使用图标 + 文字 + 底部 accent 高亮。打开快速设置时类别行直接展开为
  布局 / 视频 / 音频 / 字幕 / 播放列表 / 章节 / 插件，点击只切换页面、不会关闭侧栏；
  面板打开期间标题栏与 OSC 保持显示（操作模式）。
- 详细记录：[docs/acceptance/v1.1.2.md](docs/acceptance/v1.1.2.md)。

## 关键事实

| 项目 | 决定 |
|---|---|
| UI | WinUI 3（Windows App SDK 2.x），非 MSIX（unpackaged） |
| 播放内核 | libmpv（要求 mpv **0.41+**，固定构建见 `third_party/libmpv/VERSION.txt`） |
| 视频呈现 | mpv `--d3d11-output-mode=composition` 的 swapchain 绑定到 XAML `SwapChainPanel`，XAML UI 覆盖其上 |
| 语言/运行时 | C# / .NET 10，self-contained win-x64 |
| 界面语言 | 简体中文（默认）与 English，可在设置中即时切换 |
| 许可证 | **GPL-3.0-only**（见 [LICENSE](LICENSE)、[NOTICE](NOTICE)、[许可证审计](docs/licensing/AUDIT.md)） |
| 上游基线 | IINA `develop` @ `5c15f81568b1f61f85219302843018e3c43c8d3b`（2026-10-16） |

## 安装与运行

发布包是免安装的绿色压缩包（无需单独安装 .NET 或 Windows App Runtime）：

1. 获取 `artifacts/wiina-1.2.1-win-x64.zip`（由下方打包命令生成，或由维护者随 Release 提供）。
2. 解压到任意可写目录。
3. 运行 `Wiina.exe`。设置、日志与插件目录位于 `%LOCALAPPDATA%\Wiina`。
4. 首次启动可拖入文件/文件夹/URL，或使用欢迎窗口打开媒体。

可选：`installer/wiina.iss` 是 Inno Setup 6 的**可选**安装脚本；本仓库不构建或提交安装包，
便携 zip 是 v1 的正式分发形式。

## 构建

前置：

- Windows 11 22H2+（Windows 10 2004+ 为尽力支持）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- mpv 0.41+ 的 `libmpv-2.dll`：运行 `tools/fetch-libmpv.ps1`（下载到 `third_party/libmpv/`，该目录不入库）
- 可选：Visual Studio 2026（C# 桌面开发）+ Windows App SDK

```powershell
# 1) 取 libmpv
powershell -ExecutionPolicy Bypass -File tools/fetch-libmpv.ps1

# 2) 构建（self-contained：目标机不需要额外安装 .NET / Windows App Runtime）
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\Wiina.slnx -c Release

# 3) 运行
.\src\Wiina.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Wiina.exe
```

> 若 `dotnet` 不在 PATH，可使用 `%USERPROFILE%\.dotnet\dotnet.exe`（用户级安装）。
> 发布构建为 `x64`；ARM64 未验证，不在 v1 范围内。

## 测试

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Release
```

测试覆盖设置/搜索/菜单规则、快捷键、OSC/OSD、播放列表/历史、滤镜/EQ、窗口布局/几何，
以及 Stage 11 的本地化目录完整性、语言解析/持久化取值、打包清单与无障碍元数据契约。

## 打包（可复现）

```powershell
pwsh -File scripts/package.ps1
```

脚本会：用 `dotnet build -c Release -r win-x64 --self-contained -p:Platform=x64 -t:Rebuild`
构建到 `src/Wiina.App/bin/x64/Release/...`（不使用 `dotnet publish`，避免 WinUI 3 unpackaged
应用出现 XBF parse 失败）→ 复制构建输出、`third_party/libmpv/libmpv-2.dll`、`LICENSE`、
`NOTICE`、`README.md` 并生成 `SOURCE.txt`（源码地址 + 提交 + 构建时间）→ 校验必需文件与
**待打包 `Wiina.dll` 的版本** → 生成 `artifacts/wiina-1.2.0-win-x64.zip`。`artifacts/` 与
`third_party/libmpv/` 均被 gitignore，仓库不提交二进制。缺少任一必需文件或版本不匹配时脚本直接失败。

## 本地化与无障碍

- 运行时字符串目录位于 `src/Wiina.Core/Localization/UiStringCatalog.cs`（zh-Hans/en 成对），
  设置目录的英文覆盖在 `SettingsLocalization.cs`，菜单在 `MenuLocalization.cs`；WinUI 3
  不支持自定义 markup extension，XAML 通过 `loc:Loc.Text` 等附加属性绑定并在切换语言时重放。
- 设置 → 通用 → 界面语言可即时切换；语言选择会持久化，重启后保持。主界面与设置窗口即时刷新，
  已打开的辅助窗口（欢迎/关于/日志/检查器/滤镜）在重新打开后使用新语言。
- 高对比度主题映射到系统颜色；`AnimationsEnabled` 与系统“显示动画”同时生效；播放器画面与 OSC
  固定为从左到右，避免 RTL 系统镜像进度/音量。详见 [docs/accessibility.md](docs/accessibility.md)。
- 深度技术字符串（检查器字段名、滤镜/编码器名称、调试/日志）保持原文，不机器翻译；
  覆盖范围与例外见 [docs/accessibility.md](docs/accessibility.md#localization-coverage)。

## 性能与已知问题

- 启动/内存实测方法与数字见 [docs/performance.md](docs/performance.md)。
- v1 已知问题与跳过范围见 [docs/release/known-issues.md](docs/release/known-issues.md)。
- 插件 API **未实现**（Stage 9 跳过），见 [docs/plugins/README.md](docs/plugins/README.md)。

## 文档

- [实施计划](docs/PLAN.md) — 阶段、范围、验收标准
- [Stage 11 验收](docs/acceptance/stage-11.md) — §19.2 清单与结果
- [无障碍与本地化说明](docs/accessibility.md)
- [性能测量](docs/performance.md)
- [发布说明](docs/release/notes.md) · [已知问题](docs/release/known-issues.md)
- [许可证审计](docs/licensing/AUDIT.md) — GPL/mpv/字体/图标/商标义务
- [ADR-0001 视频管线](docs/architecture/0001-video-pipeline.md) — 为什么用 composition swapchain
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
