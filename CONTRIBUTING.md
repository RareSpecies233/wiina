# 贡献指南

## 开始之前

- 先读 [README](README.md)、[许可证审计](docs/licensing/AUDIT.md)、[ADR-0001](docs/architecture/0001-video-pipeline.md)、
  [ADR-0004](docs/architecture/0004-solid-chrome.md)。[实施计划](docs/PLAN.md) 是 v1 前的历史文档，
  只用于了解设计意图，不代表当前行为。
- 本项目以 **GPL-3.0-only** 发布。提交即表示你同意以该许可证授权你的贡献。
- 不提交 IINA 的 Swift/AppKit 源码、`.xcassets` 图片、SF Symbols、IINA 图标或任何专有字体。
- 不提交 mpv/libmpv 二进制；用 `tools/fetch-libmpv.ps1` 获取。

## 开发环境

1. Windows 11 22H2+（Windows 10 2004+ 尽力支持）
2. [.NET 10 SDK](https://dotnet.microsoft.com/download)
3. 可选：Visual Studio 2026（“.NET 桌面开发” + “Windows 应用开发”）
4. libmpv：

```powershell
powershell -ExecutionPolicy Bypass -File tools/fetch-libmpv.ps1
```

5. 构建：

```powershell
dotnet build src/Wiina.App/Wiina.App.csproj -c Debug -p:Platform=x64
dotnet test tests/Wiina.Core.Tests/Wiina.Core.Tests.csproj -c Release   # 876 tests
```

> 解决方案 `Wiina.slnx` 也带 x64 平台映射，但 `dotnet build Wiina.slnx -p:Platform=x64` 在当前 SDK 下会因
> 平台名不被识别而失败；请按上面的项目构建，或用 `scripts/dev-build.ps1`。测试当前 876 项。

## 代码约定

- C#：`Nullable` 开启；文件级 namespace；异步方法以 `Async` 结尾；UI 线程只做 UI。
- XAML：`x:Bind` 优先；资源放 `ResourceDictionary`；颜色/尺寸用令牌，不写魔法数。
- 不阻塞 UI 线程：mpv 命令走异步/事件线程；大列表后台解析。
- 动画：尊重 `UISettings.AnimationsEnabled` / “减少动画”。
- 可访问性：交互控件必须有 `AutomationProperties.Name`；支持键盘焦点与高对比度。
- 日志：不记录媒体内容/路径中的隐私信息；错误日志包含可复现信息。

## 提交与 PR

- 分支：`stage/N-<slug>`（阶段工作）或 `fix/<slug>`、`feat/<slug>`。
- Commit：Conventional Commits，例如 `feat(osc): add floating controller drag snap`。
- 一个 PR 只做一件事；阶段 PR 关联 `docs/acceptance/stage-N.md`。
- PR 模板中的验收清单必须逐项确认。

## Stage 11：本地化 / 无障碍 / 打包

- 界面字符串：`src/Wiina.Core/Localization/UiStringCatalog.cs`（zh-Hans/en 成对，当前 581 条）；
  设置行、菜单、OSD 分别有 `SettingsLocalization.cs`、`MenuLocalization.cs`、`OsdMessage.cs` 覆盖。
  两种语言必须同时补齐；缺英文或英文残留中文会让单元测试失败。XAML 用
  `src/Wiina.App/Localization/Loc.cs` 的附加属性（`loc:Loc.Text`、`loc:Loc.AutomationName` 等），
  不写死中文。
- 例外：检查器字段名、编解码器/滤镜名、日志等深度技术字符串按设计不翻译，见
  `docs/accessibility.md`。
- 纯图标按钮必须有 `AutomationProperties.Name`；新动画必须同时尊重
  `AnimationsEnabled` 和系统动画设置；播放器画面/OSC 固定从左到右。
- 打包：`pwsh -File scripts/package.ps1 -Version 1.3.6`（会校验 `Wiina.dll` 版本与便携卸载器）。修改清单时同步更新
  `src/Wiina.Core/Release/PackageManifest.cs` 与脚本，`Stage11ReleaseTests` 会校验一致性；
  打包器还会校验 `Wiina.dll` 的文件版本必须等于 `-Version`。
- 新增纯逻辑（吸附、几何、文件关联目录等）请配 Core 测试；现有 `EdgeSnapTests`、
  `FileAssociationCatalogTests`、`RenderSizeTests` 是范例。
- **v1 范围**：Stage 9（插件）与 Stage 10（在线字幕/yt-dlp）明确跳过；不要提交只启用入口、
  却没有完整功能的半成品。

## 许可证与来源

- 新依赖：MIT/BSD/Apache-2.0 可直接用；GPL 兼容；禁止 CC-BY-NC/SSPL/BUSL/无许可证代码。
- 新图标：原创或 GPL 兼容图标集（记录来源与许可）。
- 新字体：仅系统字体或 OFL/Apache；不提交 Microsoft/Apple 专有字体。
- mpv.net 配置导出：`resources/mpvnet/` 只放纯文本（`wiina.conf` / `wiina-input.conf`）；改选项或键位时与
  `src/Wiina.Core/Input/wiina-defaults.conf` 和 `docs/reference/iina-shortcuts.md` 同步。
- 测试媒体：不提交版权媒体；用 mpv `testsrc` 或 CC-BY 样本。
