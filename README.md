# Wiina

> **工作名 / Working name.** Windows 11 原生、IINA 风格界面、mpv 内核的播放器。
> WinUI 3 · Windows App SDK · libmpv · C# / .NET 10

Wiina 把 [IINA](https://iina.io/) 在 macOS 上的**界面逻辑**（窗口层级、OSC、侧边栏、OSD、模式与状态机）带到 Windows，并用
[mpv](https://mpv.io/) 作为播放内核。视觉上不照搬 AppKit，而是按 Windows 11 Fluent 规范重新设计：Segoe UI Variable、
Segoe Fluent Icons、Mica/Acrylic、圆角与系统主题。

> **状态：Stage 0–3 已验收；Stage 4 已实现，待验收。** 计划已冻结；当前仓库可运行
> WinUI 3 + mpv composition swapchain 播放器，包含文件/URL/拖放、轨道/字幕/章节/播放列表、
> 键鼠/滚轮、基础 OSD、历史与续播、A-B/截图/逐帧、可自动隐藏的 WinUI 3 扩展标题栏、真实 OSC，
> 以及带四页快速设置/播放列表/章节的左右侧边栏。
> 验收记录：[Stage 0](docs/acceptance/stage-0.md) · [Stage 1](docs/acceptance/stage-1.md) ·
> [Stage 2](docs/acceptance/stage-2.md) · [Stage 3](docs/acceptance/stage-3.md) ·
> [Stage 4](docs/acceptance/stage-4.md)。后续按 [实施计划](docs/PLAN.md) 的 Stage 5–11 推进。

## 关键事实

| 项目 | 决定 |
|---|---|
| UI | WinUI 3（Windows App SDK 2.x），非 MSIX（unpackaged） |
| 播放内核 | libmpv（要求 mpv **0.41+**） |
| 视频呈现 | mpv `--d3d11-output-mode=composition` 的 swapchain 绑定到 XAML `SwapChainPanel`，XAML UI 覆盖其上 |
| 语言/运行时 | C# / .NET 10 |
| 许可证 | **GPL-3.0-only**（见 [LICENSE](LICENSE)、[NOTICE](NOTICE)、[许可证审计](docs/licensing/AUDIT.md)） |
| 上游基线 | IINA `develop` @ `5c15f81568b1f61f85219302843018e3c43c8d3b`（2026-10-16） |

## 文档

- [实施计划](docs/PLAN.md) — 阶段、范围、验收标准
- [Stage 0 验收](docs/acceptance/stage-0.md) — 视频管线风险验证结果
- [许可证审计](docs/licensing/AUDIT.md) — GPL/mpv/字体/图标/商标义务
- [ADR-0001 视频管线](docs/architecture/0001-video-pipeline.md) — 为什么用 composition swapchain
- [IINA macOS UI 复刻规格](docs/reference/IINA-macOS-UI-复刻规格.md) — 输入规格（GPL-3.0）

## 构建（Stage 0 起可用）

前置：

- Windows 11 22H2+（Windows 10 2004+ 为尽力支持）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- mpv 0.41+ 的 `libmpv-2.dll`：运行 `tools/fetch-libmpv.ps1`（下载到 `third_party/libmpv/`，该目录不入库）
- 可选：Visual Studio 2026（C# 桌面开发）+ Windows App SDK

```powershell
# 1) 取 libmpv
powershell -ExecutionPolicy Bypass -File tools/fetch-libmpv.ps1

# 2) 构建（self-contained：目标机不需要额外安装 .NET / Windows App Runtime）
dotnet build Wiina.slnx -c Debug

# 3) 运行
.\src\Wiina.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Wiina.exe
```

> 首次构建会从 NuGet 还原 Windows App SDK。若 `dotnet` 不在 PATH，可使用
> `%USERPROFILE%\.dotnet\dotnet.exe`（用户级安装）或安装系统级 .NET 10 SDK。

## 许可证与声明

- 本项目以 **GNU GPL v3.0 only** 发布。见 [LICENSE](LICENSE)。
- 本项目**不是** IINA 或 mpv 的官方项目，与二者无隶属关系；“IINA” 名称与图标归其作者所有，本项目不使用。
- IINA 以 GPL-3.0 发布；本项目的界面行为参考其设计，Swift/AppKit 源码未被复制。
- mpv 以 GPL-2.0-or-later 发布；libmpv 仅在以 `-Dgpl=false` 构建时才是 LGPL-2.1-or-later。发布包附带 mpv 的许可证与源码获取方式。
- SF Symbols 仅授权用于 Apple 平台；本项目改用 Segoe Fluent Icons / 自绘 SVG。
- 第三方组件与完整义务见 [NOTICE](NOTICE) 与 [许可证审计](docs/licensing/AUDIT.md)。
