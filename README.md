# Wiina

**把 IINA 的体验，带到 Windows。**
Windows 原生播放器 · [WinUI 3](https://learn.microsoft.com/windows/apps/winui/winui3/) + [libmpv](https://mpv.io/) · C# / .NET 10

[官网（GitHub Pages）](https://rarespecies233.github.io/wiina/) · [下载最新版](https://github.com/RareSpecies233/wiina/releases/latest) · [文档地图](#文档地图) · [更新日志](docs/release/notes.md)

[![Release](https://img.shields.io/github/v/release/RareSpecies233/wiina?style=flat-square&color=ff8a5c)](https://github.com/RareSpecies233/wiina/releases/latest)
[![License](https://img.shields.io/badge/license-GPL--3.0--only-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-0078D4?style=flat-square)](#下载与安装)
[![Tests](https://img.shields.io/badge/tests-943%20passing-2ea44f?style=flat-square)](tests/Wiina.Core.Tests)
[![WinUI 3](https://img.shields.io/badge/UI-WinUI%203-8a5cf6?style=flat-square)](https://learn.microsoft.com/windows/apps/winui/winui3/)

![Wiina 播放 1080p HEVC，悬浮 OSC 与深色标题栏](site/assets/hero.webp)

Wiina 是一个 **WinUI 3 原生**的桌面播放器外壳，播放内核交给 **libmpv**。它不复刻 AppKit，而是把
[IINA](https://iina.io/) 的窗口层级、OSC、侧栏、OSD、小窗与状态机按 Windows 11 Fluent 规范重新实现：
Segoe UI Variable、Fluent Icons、Mica/Acrylic、圆角、深浅主题与高对比度。

> 不是网页套壳，也不是 mpv 换皮。视频走 mpv 的 `d3d11` 合成，界面是真正的 XAML；源码全部公开。

---

## 为什么是 Wiina

| 痛点 | Wiina 的做法 |
| --- | --- |
| **IINA 只在 macOS** | 把它的交互模型（OSC、侧栏快速设置、OSD、模式状态机）在 Windows 上重做，并适配鼠标/键盘/触控板 |
| **mpv 很强，但太“素”** | 保留 mpv 的解码、滤镜、字幕与画质能力，补上完整桌面 UI：设置搜索、快捷键编辑器、播放列表、章节、统计面板 |
| **主流播放器界面停在十年前** | 原生 WinUI 3、高 DPI、深/浅/高对比度、减少动态、Mica 与圆角，全部跟随系统 |
| **字幕/预览总差一点** | 同集外挂字幕全部入列、assfonts 优先、双字幕；缩略图预生成 + 磁盘缓存 + 即时预览 |
| **小窗/画中画割裂** | 无边框 PiP、快捷键与主窗一致、贴边吸附、小窗期间自动挂起毛玻璃抓帧 |

---

## 功能亮点

- **播放与画质**：libmpv 0.41、`gpu-next` + `d3d11` 合成、硬解、HDR 色调映射、10 段均衡器、音频滤镜；
  23 项画质细调，内置「默认 / 性能优先」两档，可导入导出。
- **OSC 与侧栏**：悬浮/停靠 OSC、2.5 秒自动隐藏（可调 0.5–30 s）、5 px 抖动过滤、左键点击隐藏、
  侧栏快速设置（布局/视频/音频/字幕/播放列表/章节/插件）。
- **字幕**：ASS/SSA 特效、字体/描边/位置/延迟、双字幕、同集多版本候选、assfonts 优先、自动加载与记忆。
- **预览与统计**：时间轴缩略图（预生成 / 即时预览、磁盘 LRU、可调延迟）、原生统计面板（文件/视频/音频/播放/渲染）、
  丢帧卡片与设备丢失恢复。
- **小窗**：迷你播放器（开发中）、无边框画中画、置顶/吸附、快捷键转发、毛玻璃抓帧挂起。
- **桌面集成**：文件关联（视频/音频、管理员一键注册）、默认应用跳转、单实例、拖放打开、命令行/最近文件。
- **可访问性**：11 页设置全部可键盘操作、HighContrast、减少动态、zh-Hans/en 本地化、焦点可视化。

### 截图

| 窗口播放 | 快速设置 | 播放统计 |
| --- | --- | --- |
| ![窗口播放](site/assets/hero.webp) | ![侧栏快速设置](site/assets/sidebar.webp) | ![统计面板](site/assets/stats.webp) |

| 画中画 |
| --- |
| ![画中画](site/assets/pip.webp) |

---

## 下载与安装

前往 **[Releases](https://github.com/RareSpecies233/wiina/releases/latest)** 下载最新版本：

| 形态 | 文件 | 适合 |
| --- | --- | --- |
| 便携版 | `wiina-<version>-win-x64.zip` | 解压即用、不写安装信息；自带 `Uninstall Wiina.exe` 可清理注册表与配置 |
| 安装版 | `Wiina-Setup-<version>-x64.exe` | 开始菜单/卸载入口；卸载器会提权并可选保留配置 |

- **系统要求**：Windows 10 2004+（build 19041）或 Windows 11，x64；自带 .NET 与 Windows App Runtime，无需另装。
- **没有代码签名**：首次运行可能出现 SmartScreen「Windows 已保护你的电脑」，点 **更多信息 → 仍要运行**；
  详细说明与卸载残留清理见 [docs/installer.md](docs/installer.md)。
- **校验下载**：每个版本的 zip/setup 大小与 SHA-256 记录在 [验收页](docs/acceptance/README.md)，
  可用 `Get-FileHash <file> -Algorithm SHA256` 对比。

---

## 30 秒上手

| 操作 | 默认键 |
| --- | --- |
| 播放/暂停 | `Space` |
| 快进/快退 5 秒 | `→` / `←`（`Ctrl` 为 30 秒） |
| 音量 | `↑` / `↓`、滚轮 |
| 全屏 | `Enter` / 双击画面 |
| 快速设置侧栏 | `Shift+Alt+V`（视频）/ `A`（音频）/ `S`（字幕）/ `P`（播放列表） |
| 设置 | `Ctrl+,` |
| 截图 | `Ctrl+S` |
| 画中画 | `Ctrl+Shift+P` |
| 循环 / 置顶 / 静音 | `Ctrl+L` / `Ctrl+T` / `Ctrl+/` |

完整键位（含 mpv 原生动作）见 [docs/reference/iina-shortcuts.md](docs/reference/iina-shortcuts.md)；
应用内「设置 → 控制 → 快捷键」可搜索、录制、冲突检测、导入导出。

---

## 文档地图

| 想知道 | 去哪里 |
| --- | --- |
| 视频如何从 mpv 到 WinUI | [docs/architecture/0001-video-pipeline.md](docs/architecture/0001-video-pipeline.md) |
| 毛玻璃/纯色档怎么实现 | [docs/architecture/0003-acrylic.md](docs/architecture/0003-acrylic.md) · [0004-solid-chrome.md](docs/architecture/0004-solid-chrome.md) |
| IINA 交互规格 | [docs/reference/IINA-macOS-UI-复刻规格.md](docs/reference/IINA-macOS-UI-复刻规格.md) |
| 缩略图完整逻辑 | [docs/reference/thumbnails.md](docs/reference/thumbnails.md) |
| 设置页/行清单 | [docs/reference/settings-layout.md](docs/reference/settings-layout.md) |
| 快捷键与 IINA 对照 | [docs/reference/iina-shortcuts.md](docs/reference/iina-shortcuts.md) |
| 每个版本验收了什么 | [docs/acceptance/README.md](docs/acceptance/README.md) |
| 发布说明与已知问题 | [docs/release/notes.md](docs/release/notes.md) · [known-issues.md](docs/release/known-issues.md) |
| 安装、卸载、文件关联 | [docs/installer.md](docs/installer.md) · [file-associations.md](docs/file-associations.md) |
| 性能与卡顿分析 | [docs/performance.md](docs/performance.md) · [docs/reports/](docs/reports) |
| 参与开发 | [CONTRIBUTING.md](CONTRIBUTING.md) |

---

## 仓库结构

```text
src/
  Wiina.App/          WinUI 3 外壳：窗口、控件、视图、服务、Win32 互操作
  Wiina.Core/         纯逻辑：设置目录、状态机、输入/快捷键、字幕、缓存、播放策略
  Wiina.Mpv/          libmpv 绑定与播放器封装（属性、事件、截图、轨道）
  Wiina.Uninstall/    提权卸载器（单文件、无 UI 依赖）
tests/
  Wiina.Core.Tests/   xUnit：几何、吸附、OSC、快捷键、缓存、设置等
docs/
  architecture/       架构决策（视频管线、标题栏、毛玻璃、纯色档）
  acceptance/         逐版本人工验收 + 产物校验和
  reference/          IINA 规格、缩略图、设置布局、快捷键
  release/            发布说明、已知问题、发布流程
  reports/            卡顿/性能/缩略图专题报告
installer/            Inno Setup 脚本 + 简体中文语言包
resources/mpvnet/     给 mpv.net 的 wiina.conf / wiina-input.conf
scripts/              dev-build / package / release
site/                 GitHub Pages 落地页（纯静态）
tools/                libmpv 获取等开发脚本
```

---

## 构建与测试

前置：.NET 10 SDK、Windows 10 2004+；libmpv 由脚本获取（`third_party/` 不提交二进制）。

```powershell
# 首次：抓取 libmpv
pwsh -File tools/fetch-libmpv.ps1

# 构建 app（注意：不是解决方案级 -p:Platform）
dotnet build .\src\Wiina.App\Wiina.App.csproj -c Release -p:Platform=x64

# 单元测试
dotnet test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Release

# 或使用仓库脚本（串行构建锁 + 日志）
pwsh -File scripts/dev-build.ps1 -Agent me -Test

# 打包 portable zip / 安装包
pwsh -File scripts/release.ps1 -Version 1.3.9
```

验收基线：**943 项测试通过**、Release 构建 **0 警告 / 0 错误**、便携包与安装包冒烟通过。
详见 [docs/acceptance/v1.3.9.md](docs/acceptance/v1.3.9.md)。

---

## 项目状态

- 当前版本 **v1.3.9**（2026-10-25），前置版本见 [更新日志](docs/release/notes.md)。
- 设置 **11 页 / 133 行**；本地化 **581** 个字符串键。
- 已实现：播放管线、原生标题栏、OSC/侧栏、播放列表/章节/历史、字幕、缩略图、统计面板、
  全屏/PiP/迷你、文件关联、快捷键编辑器、字幕字体、滤镜/均衡器、HDR、单实例与拖放。
- 未实现（明确跳过）：插件宿主、在线字幕、yt-dlp。相关入口保持可见但禁用并说明原因。
- 已知限制：`Always` 毛玻璃档在主窗可见时仍按 ~8 Hz 抓帧；混合 DPI 下部分悬停/吸附几何未覆盖；
  光标自动隐藏在少数 WinUI 输入栈环境仍可能失效。见 [known-issues.md](docs/release/known-issues.md)。

---

## 参与贡献

欢迎 Issue、功能建议与 PR。开始之前：

1. 读 [CONTRIBUTING.md](CONTRIBUTING.md) 与最近的 [验收记录](docs/acceptance/README.md)；
2. 改动逻辑请补 `tests/Wiina.Core.Tests` 用例；
3. 用户可见行为变化请同步 `docs/`（验收页、参考文档或发布说明）；
4. 提交前跑 `pwsh -File scripts/dev-build.ps1 -Agent <name> -Test`。

---

## 许可证与致谢

- **GPL-3.0-only**，见 [LICENSE](LICENSE) 与 [NOTICE](NOTICE)。
- 界面交互灵感来自 [IINA](https://github.com/iina/iina)（GPL-3.0）；未复制其 Swift/AppKit 源码或图像资源。
- 播放内核：[mpv](https://github.com/mpv-player/mpv) / libmpv（GPL-2.0-or-later）。
- 界面框架：WinUI 3 / Windows App SDK。
- Wiina 与 IINA、mpv 项目均无隶属关系。
