# 贡献指南

## 开始之前

- 先读 [实施计划](docs/PLAN.md)、[许可证审计](docs/licensing/AUDIT.md)、[ADR-0001](docs/architecture/0001-video-pipeline.md)。
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
dotnet test
```

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

## 许可证与来源

- 新依赖：MIT/BSD/Apache-2.0 可直接用；GPL 兼容；禁止 CC-BY-NC/SSPL/BUSL/无许可证代码。
- 新图标：原创或 GPL 兼容图标集（记录来源与许可）。
- 新字体：仅系统字体或 OFL/Apache；不提交 Microsoft/Apple 专有字体。
- 测试媒体：不提交版权媒体；用 mpv `testsrc` 或 CC-BY 样本。
