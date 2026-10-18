# 许可证与合规审计（v0.1）

> 结论先行：**Wiina 以 GPL-3.0-only 发布**。不复制 IINA 的 Swift/AppKit 源码、图片资产、SF Symbols 或应用图标；
> 不把 mpv/libmpv 二进制提交进 git；发布包附带 mpv 的许可证文本与源码获取方式。
> 本文件不是法律意见；发布前建议做一次人工复核。

---

## 1. 组件清单

| 组件 | 来源/版本 | 许可证 | 用法 | 是否随仓库分发 | 义务 |
|---|---|---|---|---|---|
| IINA | `iina/iina` @ `5c15f81` | **GPL-3.0**（仓库 `LICENSE`，无 “or later” 授权声明） | 仅参考其界面行为；规格文档由源码推导 | 规格文档随仓库（GPL-3.0）；**不含源码/资产** | 保留版权与许可证声明；整体 GPL-3.0 |
| mpv / libmpv | mpv v0.41+；shinchiro `mpv-dev-x86_64` 构建 | mpv 默认 **GPL-2.0-or-later**；仅 `-Dgpl=false` 构建为 LGPL-2.1-or-later | 播放/解码/字幕/音频/硬件解码 | **不提交**；由 `tools/fetch-libmpv.ps1` 下载；发布包可附带 DLL | 附 GPL/LGPL 文本、版权声明、源码获取方式；允许替换库 |
| Windows App SDK | 2.5.x（NuGet） | MIT | WinUI 3 / 窗口 / 合成 | 通过 NuGet 还原 | 保留版权与许可文本 |
| Microsoft.Windows.SDK.BuildTools | 10.0.28000.x | Microsoft 专有可再分发（构建工具） | 构建 | NuGet | 按 Microsoft 条款 |
| CommunityToolkit.Mvvm | 8.4.x | MIT | MVVM 源生成器 | NuGet | 保留声明 |
| Microsoft.Windows.CsWin32 | 0.3.x | MIT | Win32 P/Invoke 源生成 | NuGet | 保留声明 |
| .NET 10 | Microsoft | MIT | 运行时/基础库 | 发布包 self-contained 含运行时 | 保留声明 |
| xUnit.net / Test SDK / coverlet | 测试 | Apache-2.0 / MIT / MIT | 仅测试 | 否 | 测试目录保留声明 |
| Segoe UI Variable / Segoe Fluent Icons | Windows 系统字体 | Microsoft 系统字体许可 | 运行时使用 | **不重分发** | 不打包字体文件 |
| SF Symbols | Apple | Apple 专有，仅限 Apple 平台 | **不使用** | 否 | — |
| IINA 图标/`.xcassets` | IINA | GPL-3.0 + 商标 | **不使用** | 否 | — |
| WebView2 | Microsoft（v2 插件） | Microsoft 可再分发运行时 | 插件 UI（v2） | 运行时依赖 | 附 Microsoft 条款 |
| yt-dlp（v2） | Unlicense（公共领域） | Unlicense | URL 解析（v2） | 可选下载，不提交 | 保留 Unlicense 文本 |
| OpenSubtitles API（v2） | 服务 | 服务条款 + API 许可 | 在线字幕（v2） | 否 | 用户自备 API key；遵守 ToS |

---

## 2. GPL-3.0-only 的分发义务（桌面应用）

发布二进制时（zip/安装器）必须同时满足：

1. **提供完整对应源码**：本仓库的 tag/commit；若修改了 mpv，提供修改后的 mpv 源码。最稳妥：发布页同时给出
   `wiina-<version>-src.zip` 与 mpv 对应版本源码链接/归档。
2. **附许可证文本**：根目录 `LICENSE`（GPL-3.0）、`NOTICE`、`licenses/` 下的 mpv GPL/LGPL 文本与第三方声明。
3. **保留版权声明**：源码文件头、关于页、NOTICE 中保留 contributors 与上游版权。
4. **不得附加额外限制**：不能禁止用户反向工程/修改/再分发；EULA 不得与 GPL 冲突。
5. **安装信息（GPLv3 §6）**：用户必须能替换/重新链接库。我们动态加载 `libmpv-2.dll`，用户可替换同 ABI 的构建；
   文档说明 DLL 放置位置与替换方法。
6. **专利许可（GPLv3 §11）**：贡献者授予专利许可；不要引入仅限“不可商用”的第三方代码。
7. **商标**：GPL 不授予商标权；不得暗示 IINA/mpv 官方背书。

> 采用 GPL-3.0-only 而非 “or later”：IINA 的仓库级授权未声明 “or later”；我们的衍生/参考关系下，v3-only 最保守。
> mpv 的 “GPLv2 or later” 允许我们选择 v3，因此组合许可为 GPL-3.0-only。

---

## 3. IINA 专项

| 事项 | 决定 | 说明 |
|---|---|---|
| Swift/AppKit 源码 | **不复制** | 以规格文档的行为描述为输入，用 C#/WinUI 重写 |
| 规格文档 | 随仓库，GPL-3.0 | 由 IINA 源码推导，属衍生物；与项目同许可 |
| 界面布局/交互 | 参考（行为不受版权保护） | 层级、顺序、状态机、触发条件可复刻 |
| 图标/图片/`Assets.xcassets` | **不使用** | 用 Segoe Fluent Icons + 自绘 SVG |
| 本地化文案 | **不整包复制** | 自行撰写 zh-Hans/en；若借用短句，在 NOTICE 注明并保持 GPL-3.0 |
| 默认键位 `iina-default-input.conf` | 参考规格表重写为 Windows 键位 | 若直接改编，注明来源（GPL-3.0） |
| 名称/图标/域名 | **不使用** | “Wiina” 为独立工作名；README/关于页声明 not affiliated |
| 应用图标 | 自绘/委托设计 | 不使用 IINA 图标或其变形 |

**商标风险**：“Wiina” 与 “IINA” 视觉/读音接近。若公开发布/商业化，建议：
- 明确副标题 “an independent, IINA-inspired player”；
- 关于页与 README 显著声明 “not affiliated with or endorsed by IINA”；
- 不使用 IINA 的图标、配色组合或域名混淆；
- 如收到权利人异议，准备改名（代码中名称集中在 `Branding` 常量，便于替换）。

---

## 4. mpv / libmpv 专项

- **动态链接**：通过 P/Invoke 加载 `libmpv-2.dll`；不静态链接。GPL 下动态链接不改变整体 GPL 义务（本项目已是 GPL-3.0）。
- **版本要求**：mpv ≥ 0.41（`--d3d11-output-mode=composition`、`display-swapchain`、`--d3d11-composition-size`）。
- **构建来源**：shinchiro 的 `mpv-dev-x86_64` 是默认（GPL）构建，含 GPL-only 组件；按 GPL-2.0-or-later 处理，本项目选择 v3。
- **不提交二进制**：仓库只保留 `tools/fetch-libmpv.ps1`（固定 release + SHA256）。CI 不需要 DLL 即可构建。
- **发布包**：
  - `libmpv-2.dll` 原样附带（不修改）；
  - `licenses/mpv/LICENSE.GPL`、`LICENSE.LGPL`、`Copyright`；
  - 记录确切构建 tag（例如 `20261018-git-01e2533f31`）与下载 URL；
  - 提供 mpv 源码获取方式（上游 tag + 构建脚本链接）；若我们未修改 mpv，指向上游即可。
- **可替换性**：README 说明把同 ABI 的 `libmpv-2.dll` 放到 `runtimes/win-x64/native/` 即可替换；不校验签名/哈希阻止替换。
- **编解码器专利**：H.264/HEVC/AV1 等专利许可与 GPL 无关；本项目不额外捆绑编解码器，使用 mpv 构建自带能力；
  README 注明“编解码器专利许可由分发者/用户自行评估”。不要把“GPL”误解为“专利免费”。

---

## 5. 字体、图标、媒体

- **Segoe UI Variable / Segoe Fluent Icons**：Windows 系统字体，运行时使用，不复制字体文件进仓库/安装包。
- **SF Symbols**：Apple 许可限制在 Apple 平台；**禁止**在 Windows 应用中使用其字形、PDF/SVG 导出或改名副本。
- **自绘图标**：原创 SVG，GPL-3.0；在 `Assets/Icons/LICENSE` 注明作者与许可。
- **测试媒体**：不提交版权媒体；开发用 mpv `testsrc`/`sine`，需要真实样本时用 CC-BY（如 Big Buck Bunny）并记录来源。
- **截图/录屏**：验收文档中的截图若含第三方影片画面，发布前替换为测试图案或 CC 素材。

---

## 6. 依赖引入规则（给后续贡献者）

1. 新 NuGet 依赖：MIT/BSD/Apache-2.0 可直接用；LGPL 动态链接需评估；GPL 兼容；**禁止** CC-BY-NC、SSPL、BUSL、
   商业限制、无许可证代码。
2. 新图标：必须原创或来自 GPL 兼容的图标集（如 Lucide ISC、Material Symbols Apache-2.0），记录来源与许可。
3. 新字体：只允许系统字体或 OFL/Apache 字体；不得提交 Microsoft/Apple 专有字体。
4. 新原生库：优先动态加载；记录版本、许可、SHA256、源码链接；不得提交无源码的二进制 blob（mpv 例外，按 §4 处理）。
5. 代码来源：不复制 Stack Overflow/博客代码，除非许可明确且兼容；不复制 IINA/SF Symbols 资产。

---

## 7. 发布合规检查清单

- [ ] `LICENSE`（GPL-3.0）在根目录与安装包内
- [ ] `NOTICE` 包含 IINA、mpv、Windows App SDK、CommunityToolkit、CsWin32、.NET、字体说明
- [ ] `licenses/` 含 mpv 的 GPL/LGPL 文本与 Copyright；含第三方 NuGet 许可
- [ ] 发布页提供对应源码（本仓库 tag + mpv 源码获取方式）
- [ ] 安装包不包含 IINA 资产、SF Symbols、Segoe 字体文件
- [ ] `libmpv-2.dll` 版本/构建 tag 记录在 release notes
- [ ] 应用内“关于/许可证”页可查看上述信息
- [ ] 未新增 GPL 不兼容依赖
- [ ] 商标声明可见（README + 关于页）
- [ ] 测试媒体/截图无未授权版权内容

---

## 8. 待确认/风险

1. **shinchiro 构建的确切许可证文本**：发布前下载对应 7z，确认包内 `LICENSE.GPL`/`Copyright` 与构建选项；按其文本原样附带。
2. **名称商标**：见 §3；若计划商业化，建议先做商标检索或改名。
3. **插件系统（v2）**：插件 API 的许可证、沙箱、第三方插件分发需单独审计；不要让插件绕过 GPL 义务。
4. **在线服务（v2）**：OpenSubtitles/yt-dlp 的 ToS、API key、速率限制；不要内置共享 key。
5. **HDR/杜比视界**：杜比视界元数据/专利另有限制；v1 不做。
6. **CI 构建产物**：不要把 `libmpv-2.dll` 作为 artifact 公开发布，除非同时满足 §4 的源码/许可义务。

---

## 9. 参考

- IINA 仓库与许可证：https://github.com/iina/iina/blob/develop/LICENSE
- mpv Copyright：https://github.com/mpv-player/mpv/blob/master/Copyright
- mpv 手册（libmpv / d3d11 composition）：https://mpv.io/manual/master/
- Windows App SDK 许可证：https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE
- GNU GPLv3 常见问题：https://www.gnu.org/licenses/gpl-faq.html
- SF Symbols 许可：https://developer.apple.com/sf-symbols/
- shinchiro 构建：https://github.com/shinchiro/mpv-winbuild-cmake
