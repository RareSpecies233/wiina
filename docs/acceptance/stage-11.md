# Stage 11 — 打磨与发布（验收包）

- **分支**：`stage/11-release`（本分支 HEAD）
- **版本**：1.0.0
- **日期**：2026-10-19
- **环境**：Windows 11 25H2 26200；.NET SDK 10.0.402；
  Windows App SDK 2.5.1；libmpv 0.41.0（客户端 API 2.6）。
- **范围说明**：维护者要求**跳过 Stage 9（插件）与 Stage 10（在线字幕/yt-dlp）**，
  直接进入 Stage 11；本包不包含这两阶段的功能，相关菜单/设置行保持可见但禁用并注明原因。

## 1. 交付范围

### 1.1 本地化（zh-Hans / en）

- 运行时语言层：`AppLanguage` / `Loc` / `UiStringCatalog` / `MenuLocalization` /
  `SettingsLocalization`，覆盖主窗口/标题栏/OSC、侧边栏、设置导航/页面标题/
  行标题与描述、菜单、欢迎/关于/日志/检查器/打开 URL 对话框、常见 OSD 与错误。
- 语言设置行启用：`system` / `zh-Hans` / `en`；主 shell 与设置窗口即时重渲染，
  辅助窗口重新打开时应用；持久化到 `AppSettings.Language`。
- 510 个 catalog 键；`Stage11LocalizationTests` 校验 zh/en 键完整性与语言解析。
- 技术性字符串（检查器字段、编解码器/滤镜名、EQ 预设名、原始日志）保留中文/技术形式，
  在 `docs/release/known-issues.md` 明确说明。

### 1.2 无障碍 / 高对比度 / 减少动态

- 图标按钮、OSC、侧栏标签、设置导航/分区圆点、时间轴/音量等补
  `AutomationProperties.Name/HelpText`；`AccessibilityMetadata` + 测试校验 XAML 接线。
- 键盘：Tab/Shift+Tab、Space/Enter、F11/Esc、设置搜索 Enter/Esc、按键录制 Esc/Backspace。
- 高对比度：`Tokens.xaml` 增加 `HighContrast` 主题字典，shell 画刷使用系统颜色；
  Acrylic 侧栏回退为系统窗口画刷。
- 减少动态：应用设置与系统“动画效果”任一关闭时，标题栏/OSC/侧栏/OSD/PiP/迷你过渡即时完成。
- 限制：无专用屏幕阅读器 live region；RTL 不翻译/不镜像（视频/OSC/时间轴/音量固定 LTR）。

### 1.3 DPI / 性能 / 稳定性

- 窗口 shell 统一用 DIP + `RasterizationScale`；设置/欢迎/日志/检查器/滤镜窗口
  按目标显示器 DPI 换算物理像素。
- 性能实测（Release self-contained，方法见 `docs/performance.md`）：
  - 冷启动/无媒体：窗口显示 0.53–0.95 s，Working set 194–196 MB；
  - 1080p：窗口显示 0.53–0.54 s，Working set 235–240 MB；
  - 4K：窗口显示 0.53–0.54 s，Working set 259–266 MB；
  - Private bytes 在 4K 达 421–423 MB（GPU/swapchain 计入进程），如实记录；
  - 启动目标 <3 s 达成；idle Working set <300 MB 达成；4K Private bytes 超 300 MB 已说明。
- 缩略图/检查器在后台读取；UI 线程无同步 IPC/文件阻塞。
- DPI 125/150/200% 与混合 DPI 移动、休眠唤醒未能在本环境自动截图（切换缩放需重启会话），
  作为手工核查项记录；HDR 为 SDR 8-bit tone-mapping，非真正 HDR10 输出。

### 1.4 打包 / GPL 合规

- `scripts/package.ps1`：Release self-contained `win-x64` → 暂存目录 → 复制
  `libmpv-2.dll`、`LICENSE`、`NOTICE`、`README.md`、`SOURCE.txt` → 
  `artifacts/wiina-<version>-win-x64.zip`；缺文件即失败；`artifacts/` 已加入 `.gitignore`。
- 包内 `SOURCE.txt` 记录版本、构建时间、commit、源码地址；libmpv/FFmpeg 动态链接，
  `NOTICE` 给出许可证与 written offer。
- 可选 `installer/wiina.iss`（Inno Setup）存在但**未构建/未随包发布**；无 MSIX。
- GPL-3.0-only；无 IINA 源码/素材/SF Symbols；无 WebView2/yt-dlp 依赖。

### 1.5 文档

- `docs/release/notes.md`、`docs/release/known-issues.md`、`docs/accessibility.md`、
  `docs/performance.md`、`docs/plugins/README.md`（明确 Stage 9 未实现）、
  `CONTRIBUTING.md`、根 `NOTICE`、`README.md` 安装/打包/状态。

## 2. 构建与测试

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\Wiina.slnx -c Release
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Release
```

- Release 构建：**0 warning / 0 error**
- Core 测试：**419 passed / 0 failed / 0 skipped**（403 + 16 项 Stage 11）

## 3. 验收清单（§19.2 汇总）

| 项目 | 结果 | 说明 |
|---|---|---|
| 播放/打开/拖放/URL/历史/续播 | ✅ | Stage 3–8 回归 |
| 标题栏/OSC/侧栏/OSD/缩略图 | ✅ | Stage 1–8 回归 |
| 轨道/字幕/章节/播放列表 | ✅ | Stage 3–8 回归 |
| 全屏/PiP/迷你/多窗口 | ✅ | Stage 6 回归 |
| 11 页设置/搜索/快捷键/菜单/欢迎/辅助窗口 | ✅ | Stage 7 回归 |
| 视频滤镜/EQ/高级音画 | ✅ | Stage 8 回归 |
| zh-Hans/en 运行时切换 | ✅ | 510 键；辅助窗口重开生效 |
| 高对比度/键盘/自动化名称/减少动态 | ✅ | 文档 + 抽查；live region 限制 |
| DPI 100/125/150/200、混合 DPI | ⚠️ 部分 | 代码按 DIP/DPI；本环境仅 200% 抽查 |
| 启动/1080p/4K 性能 | ✅ | 启动 <3s、Working set <300MB；4K private 422MB 已说明 |
| 插件系统（Stage 9） | ⏭️ 跳过 | 用户要求；无插件宿主/API/WebView2 |
| 在线字幕/yt-dlp（Stage 10） | ⏭️ 跳过 | 用户要求；无网络服务/yt-dlp/相关权限 |
| 便携 zip / 可选 Inno 脚本 | ✅ | 143.9 MB zip；Inno 未构建 |
| GPL/NOTICE/源码链接 | ✅ | GPL-3.0-only；动态 libmpv/FFmpeg |
| 文档/已知问题/贡献指南 | ✅ | release/accessibility/performance/plugins |

## 4. 打包验证

- `artifacts/wiina-1.0.0-win-x64.zip`：144,723,312 bytes（SHA-256 `3588211CB3C025052BA7C2D0807F96F2EC0057A3473F177B62FC19480D73982C`）。
- 根目录包含 `Wiina.exe`、`libmpv-2.dll`、`LICENSE`、`NOTICE`、`README.md`、
  `SOURCE.txt`，以及 self-contained .NET/WinUI 运行文件与语言资源。
- `SOURCE.txt` 记录版本、构建时间、commit、源码地址。
- 解压后 `Wiina.exe` 可启动（Lead 验收时复核）。

## 5. 已知问题 / 偏离

1. **Stage 9/10 跳过**：无插件、在线字幕、yt-dlp；相关 UI 保持禁用并说明。
2. 本地化：辅助窗口已打开时不会即时切换，重开生效；技术字段/日志保持中文/技术形式。
3. 无屏幕阅读器 live region；RTL 不支持（不镜像视频/OSC）。
4. DPI 125/150/200、混合 DPI、休眠唤醒为手工核查项，未在本环境截图。
5. HDR 为 SDR tone-mapping，非 HDR10 输出；4K Private bytes 超 300 MB（GPU/swapchain 计入）。
6. 包未签名，SmartScreen 可能警告；x64 only，ARM64 未验证；无自动更新/MSIX。
7. Inno Setup 脚本可选且未构建；正式产物只有便携 zip。
