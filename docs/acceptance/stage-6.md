# Stage 6 — 模式与多窗口（验收包）

- **分支**：`stage/6-pip`（本分支 HEAD）
- **日期**：2026-10-18
- **环境**：Windows 11 25H2 26200；.NET SDK 10.0.402；
  Windows App SDK 2.5.1；libmpv 0.41.0（`third_party/libmpv/libmpv-2.dll`）；
  桌面缩放 200%（DIP ↔ 物理像素按 `XamlRoot.RasterizationScale` 换算）。
- **测试媒体**：
  - 视频：`C:\Users\zheng\Videos\Captures\Forza Horizon 6 2026-05-17 23-21-50.mp4`
  - 音频：`%TEMP%\wiina-stage6-tone.wav`（4 s 440 Hz 单声道，运行期生成，不提交）
- **截图**：`docs/acceptance/assets/stage-6-*.png`。

## 1. 交付范围

### 1.1 Core 模式状态机

新增 `src/Wiina.Core/State/ModeStateMachine.cs`：

- `PlayerMode`：`Normal / Fullscreen / PiP / Mini`（`Models/AppEnums.cs`）。
- `ModeStateMachine`：`Current`、`Previous`、`IsTransitioning`、`Changed`；
  `Request(target, Func<PlayerMode,bool> apply)` 返回
  `ModeTransitionResult(Success, From, To, Reason, ExitPrevious)`。
- 互斥：同一时刻只有一个非 Normal 模式；任意两个不同模式之间可切换，
  从 Fullscreen/PiP/Mini 切到另一个非 Normal 模式时
  `ModePolicy.RequiresExitFirst` 为 `true`，壳层先拆掉旧 presenter/置顶/PiP 面。
- 回滚：`apply` 返回 `false` 或抛异常时，状态保持 `From`，
  并再调用一次 `apply(From)` 做 best-effort 回滚；
  `Reason = ApplyFailed/RolledBack`，`Current` 绝不落到目标模式。
- 重入保护：`apply` 内再次 `Request` 返回 `Reentrant`，不改变状态。
- `ModePolicy` 给出每个模式的置顶/隐藏主视频/是否显示 chrome/是否紧凑窗口。

### 1.2 全屏

- `MainWindow.Modes.cs` 的 `RequestMode` 是唯一入口；`F11`、双击视频、
  OSC/标题栏/更多菜单、`Esc` 全部走状态机。
- 进入：保存 `_normalFrame`；关闭置顶；`AppWindow.SetPresenter(FullScreen)`；
  标题栏折叠；视频尺寸按当前显示器重算。
- 退出：恢复 `Overlapped`；按 `AppSettings.AlwaysOnTop` 恢复置顶；
  恢复窗口 frame；标题栏/OSC/侧栏按原状态恢复。
- 多显示器：只用 `DisplayArea.GetFromWindowId(AppWindow.Id)` 当前窗口所在
  显示器；不移动窗口到其他屏幕。
- 失败回滚：presenter 设置失败/异常由状态机回滚，不会卡在
  “全屏 + 置顶” 状态。
- 20 次进出（40 次切换）桌面实测：每次均恢复到进入前的
  `540,392,1820,1112`（1280×720 物理），无布局错乱。

### 1.3 自绘画中画（PiP）

新增 `src/Wiina.App/PipWindow.xaml(.cs)`：

- 独立 WinUI 3 `Window`；无标题栏/系统 caption（`PreferredHeightOption.Collapsed`），
  顶部 28 DIP 透明拖动条，`SetTitleBar` 支持拖动。
- 16:9、默认 480×270 DIP、最小 320×180 DIP、最大 1920 宽；
  `PipGeometry` 统一计算尺寸/位置；`WindowInterop.WM_SIZING` 锁定宽高比；
  创建时按目标显示器 `RasterizationScale` 把 DIP 换算为物理像素
  （本机 200% 缩放下为 960×540 物理像素）。
- 置顶：`OverlappedPresenter.IsAlwaysOnTop`；不可最大化/最小化。
- 控制条：播放/暂停、返回主窗口、关闭；2.5 s 自动隐藏（180 ms 淡出），
  鼠标进入/移动重新显示；双击窗口返回主窗口。
- 视频迁移：新增 `Controls/MpvVideoView`，主窗口与 PiP 各持一个
  `SwapChainPanel`；进入 PiP 时主面板 `SetSwapChain(panel, 0)` 解绑，
  同一个 mpv swapchain 绑定到 PiP 面板，音频与播放进度不中断；
  返回时反向操作。**不重建 libmpv、不暂停/重新初始化。**
- 尺寸：PiP 的 `SizeChanged`/DPI 变化把 `d3d11-composition-size` 更新为
  PiP 像素尺寸；暂停状态下追加一次 `seek 0 relative exact`，强制重绘一帧，
  避免暂停时切换仍显示黑帧。
- 主窗口显示遮罩：`PipPlaceholder`（“正在画中画” + 当前标题 +
  “恢复/退出画中画”按钮），视频面板不再显示。
- 互斥：进入全屏会先关闭 PiP；进入 PiP 会先退出全屏。
- 快捷键：`Ctrl+Shift+P`（原默认）、`Ctrl+P`（新增别名）；
  OSC 工具栏 PiP 按钮已启用。

### 1.4 迷你 / 音乐模式

采用**主窗口模式**而不是第二个窗口（任务允许并已在此文档说明）：

- 新增 `src/Wiina.App/Controls/MiniPlayerView.xaml(.cs)` 覆盖层；
  紧凑窗口 400×297 DIP（72 控制区 + 16:9 封面），窗口置顶，
  隐藏标题栏/OSC/侧栏；双击或“返回主窗口”按钮恢复普通模式并恢复原 frame。
- 布局常量集中在 `Core/State/MiniPlayerLayout`：封面 ≥72、播放列表 300、
  音量气泡 148、控件动画 0.2 s、控制行 48、信息区 72。
- 封面：音频文件带内嵌封面时由 mpv 作为视频轨（`image/albumart` 标记）
  直接渲染；无封面时显示自绘渐变瓦片 + 标题首字母。封面区始终 ≥72 DIP。
- 信息：标题 + “艺术家 · 专辑”（来自新增的 `metadata` 属性观察）；
  进度行（当前/总长 + 可拖动进度条）；控制行（静音、上一首、播放 28、
  下一首、播放列表、返回）；鼠标进入显示控制区、离开显示信息区，
  0.2 s 互斥淡入淡出（尊重“减少动画”）。
- 音量气泡：宽 148、圆角 18、1 px 边框；点击静音按钮或滚轮展开，
  停止滚动 1 s 后 / 指针离开后收起；内含静音、0–130 滑条、百分比。
- 播放列表：宽 300，右侧覆盖；展开时窗口高度 +300 DIP（受工作区高度限制），
  按钮/关闭按钮切换；状态持久化到 `MusicModeShowPlaylist`。
- 自动进入：`AppSettings.AutoSwitchToMusicMode`（默认开）；
  `MediaModeAdvice.IsAudioOnly` 判定“没有非封面视频轨”，文件加载后自动进入；
  设置项关闭后不自动进入。手动退出后同一文件不会反复进入。
- 可拖动：无标题栏，用根 Grid 的指针拖动 + `WindowInterop` 光标坐标移动窗口。

### 1.5 多窗口

- `App` 从单 `_window` 改为窗口注册表：`CreateWindow` / `OpenInNewWindow`；
  每个 `MainWindow` 独立拥有 `MpvPlayer`、OSC 状态、侧栏、OSD、模式状态。
- 共享：`App.Settings`（单一 `AppSettings` 实例）+ `App.SettingsStore`；
  保存后 `SettingsChanged` 通知所有窗口重新应用主题/布局/OSD。
- `Core/State/WindowCoordinator`：注册、激活（最近激活顺序）、关闭回退、
  `ShouldExit`、`ResolveMediaTargetId`。关闭最后一个窗口时 `App.Exit()`。
- 新窗口级联 +32 DIP，避免完全覆盖旧窗口。
- 菜单：`OSC 更多`、`标题栏窗口菜单`、`More` 菜单新增
  “新建窗口 (Ctrl+N)”“在新窗口打开… (Ctrl+Shift+N)”“关闭当前窗口 (Ctrl+W)”，
  以及全屏/画中画/迷你/置顶项。
- 桌面实测：两个窗口同时播放（主窗口视频 + 第二窗口 WAV 自动进入迷你），
  关闭第二个窗口后主窗口继续播放；关闭最后一个窗口进程退出。

### 1.6 媒体键 / SMTC

- SMTC 改为 **App 级单实例**（`SmtcService`），不再每个窗口各建一个：
  播放/暂停/停止/上一首/下一首/拖动进度（`PlaybackPositionChangeRequested`）
  由 `App.RouteMedia` 路由到 `WindowCoordinator.ResolveMediaTargetId()`
  解析出的活动窗口；非活动窗口只更新注册表，不更新系统媒体卡片，
  避免两个播放器互相覆盖。
- `WM_APPCOMMAND` 媒体键（播放/暂停、播放、暂停、停止、上一首、下一首）
  在 `WindowInterop` 子类化中处理；只有 `App.IsMediaTarget(this)` 的窗口
  响应并返回 1，其余窗口交还系统。
- 窗口激活时 `PushMediaStateToSession()` 立即把当前媒体的标题/时长/
  播放状态推给 SMTC。

### 1.7 UI/快捷键集成

- `KeyMap` 新增：`Ctrl+N`（新建窗口）、`Ctrl+Shift+N`（新窗口打开）、
  `Ctrl+W`（关闭当前窗口）；`Ctrl+P` 作为 PiP 别名。
  `Ctrl+M` 仍是静音（与任务示例冲突，保留既有键位，已在已知问题说明）。
- 窗口级 `KeyboardAccelerator`：`F11`、`Esc`（不再依赖 RootGrid 焦点，
  全屏后可正常退出）。
- 模式切换时强制恢复 RootGrid 焦点，保证后续快捷键可用。

## 2. 构建与测试

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" build .\Wiina.slnx -c Debug
& "$env:USERPROFILE\.dotnet\dotnet.exe" test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Debug
```

最终结果：

- Build：**0 warnings / 0 errors**。
- Tests：**295 passed / 0 failed / 0 skipped**
  （Stage 5 的 244 + 新增 51：`ModeStateMachineTests`、
  `PipGeometryTests`、`MiniPlayerLayoutTests`、`WindowCoordinatorTests`）。

## 3. 验收清单

| # | 项目 | 结果 | 证据/备注 |
|---|---|---|---|
| 1 | Normal/Fullscreen/PiP/Mini 状态机、互斥、失败回滚 | ✅ 单测 + 代码 | `ModeStateMachineTests` 覆盖切换表/回滚/重入/事件 |
| 2 | 进入 Fullscreen/PiP/Mini 正确恢复、失败回滚、无卡死置顶/presenter | ✅ 单测 + 桌面 | `apply=false/throw` 回滚；F11/ESC 恢复 frame |
| 3 | 全屏进出 20 次无布局错乱 | ✅ 桌面 | 40 次切换全部回到 `540,392,1820,1112` |
| 4 | 标题栏/OSC/侧栏行为保留 | ✅ 桌面 | 全屏隐藏标题栏、OSC 浮层仍可用、退出恢复 |
| 5 | 置顶按模式生效并持久化 | ✅ 代码 + 单测 | 进入全屏关闭、退出按 `AlwaysOnTop` 恢复；迷你默认置顶 |
| 6 | 多显示器使用当前显示器、不跳屏 | ✅ 代码 | 全部用 `DisplayArea.GetFromWindowId` |
| 7 | PiP 独立窗口 16:9 / 480×270 / 最小 320×180 / 锁定比例 | ✅ 桌面 + 单测 | 实测 480×270 DIP；200% 缩放下 `2048,1342,3008,1882` = 960×540 物理像素 |
| 8 | PiP 无完整标题栏、置顶、控制条自动隐藏 | ✅ 桌面 | 截图：控制条隐藏后仅“画中画 · 双击返回” |
| 9 | PiP 视频迁移无黑帧、音频继续、全屏互斥 | ✅ 桌面 + 代码 | 进入/恢复均见画面；同一 swapchain；状态机互斥 |
| 10 | 主窗口 PiP 占位“正在画中画” | ✅ 桌面 | 标题 + “恢复/退出画中画”按钮 |
| 11 | 迷你窗口置顶/紧凑/封面 ≥72/标题艺术家/控制 | ✅ 桌面 + 单测 | 400×297 DIP；封面瓦片 “W”；自动进入 |
| 12 | 音量气泡 148、播放列表 300、控件 0.2 s 互斥 | ✅ 桌面 + 单测 | 常量单测；桌面展开 300 宽列表；悬停切换控制区 |
| 13 | 音频文件自动进入、可关闭设置、可恢复 | ✅ 桌面 + 单测 | WAV 打开后 `Normal -> Mini`；ESC 恢复 |
| 14 | 多窗口新建/新窗口打开/关闭当前/最后窗口退出 | ✅ 桌面 + 单测 | Ctrl+N 两个窗口、Ctrl+W、最后窗口进程退出 |
| 15 | 两个窗口同时播放不同文件互不干扰 | ✅ 桌面 | 主窗口视频 + 第二窗口 WAV 同时播放 |
| 16 | 共享设置、每窗口独立播放状态 | ✅ 代码 + 单测 | `App.Settings` + `WindowCoordinator` |
| 17 | SMTC/媒体键路由到活动窗口、不重复处理 | ✅ 代码 + 单测 | 单 SMTC 实例；`ResolveMediaTargetId`；`IsMediaTarget` |
| 18 | OSC/更多/标题栏菜单与快捷键 | ✅ 桌面 + 代码 | 工具栏 PiP 按钮、窗口模式子菜单、Ctrl+N/W/Shift+N |
| 19 | 模式切换不破坏 OSD/缩略图/侧栏互斥与自动隐藏 | ✅ 代码 | 模式分支只改可见性，不改状态机；OSD 在迷你层之上 |
| 20 | 0 errors / 0 new warnings、无新增 NuGet、无二进制提交 | ✅ | 构建输出；仅现有 WinAppSDK/xUnit |

## 4. 桌面冒烟（GUI）

启动视频窗口：

```powershell
.\src\Wiina.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Wiina.exe `
  "C:\Users\zheng\Videos\Captures\Forza Horizon 6 2026-05-17 23-21-50.mp4"
```

观察：

- 启动后标题为 `Forza Horizon 6`，视频播放正常。
- `F11`：进入全屏，窗口 rect `0,0,2560,1322`；标题栏消失；
  浮动 OSC 仍可显示；左下全屏时钟/电源信息正常。
- `Esc`：退出全屏，窗口恢复到 `540,392,1820,1112`。
- 20 次 F11 循环（40 次切换，700 ms 间隔）：偶数步均恢复
  `540,392,1820,1112`，奇数步均 `0,0,2560,1322`；无黑屏/错位。
- `Ctrl+Shift+P`：出现第二个顶层窗口，480×270 DIP
  （200% 缩放下 960×540 物理像素，`2048,1342,3008,1882`，右下角 16 px 边距）；
  PiP 内视频正常、音频连续；主窗口显示“正在画中画”占位。
- PiP 控制条 2.5 s 后自动隐藏；鼠标进入/点击重新出现；
  点击“返回主窗口”后 PiP 关闭、主窗口视频立即恢复（无黑帧）。
- PiP 内按 `Esc`：PiP 关闭，主窗口恢复普通模式，视频回到主面板。
- PiP → `F11`：trace 记录 `PiP -> Fullscreen ok=True`，PiP 先关闭再进入全屏
  （互斥路径执行成功；该次截图因另一并发会话结束 Wiina 进程而未取到）。
- `Ctrl+N`：创建第二个空窗口（标题 `Wiina`，级联 +32 DIP）。
- 第二窗口 `Ctrl+O` → 选择 `%TEMP%\wiina-stage6-tone.wav`：
  文件加载后自动 `Normal -> Mini`；窗变为 400×297 DIP
  （200% 缩放下 800×594 物理），显示渐变封面 “W”、标题；
  悬停显示控制行；点击列表按钮展开 300 DIP 播放列表并把窗口增高 300 DIP；
  `Esc` 退出迷你，恢复普通窗口。
- `Ctrl+W` 关闭第二窗口：主窗口继续播放，进程仍在；
  再 `Ctrl+W` 关闭主窗口：Wiina 进程退出（`Get-Process Wiina` 为空）。
- 冒烟结束后本任务启动的 Wiina 进程全部结束；另有一个 21:51 启动的
  `lavfi:testsrc` Wiina 进程不属于本任务，未处理。

## 5. 已知问题 / 偏差

1. **迷你/音乐模式是主窗口模式**，不是规格描述的独立窗口（任务明确允许
   “若独立窗口太重，可做主窗口模式并记录”）。好处是复用同一个
   `MpvPlayer`/swapchain；差异是窗口标题栏隐藏方式与任务栏项目仍属于主窗口。
2. **PiP 复用同一个 swapchain**，不重建 mpv；暂停时通过
   `d3d11-composition-size` + `seek 0 relative exact` 强制重绘一帧。
   本机进出均无黑帧，但极慢 GPU/驱动下仍可能有单帧闪烁。
3. **PiP 窗口无系统边框调整手柄**：通过 `WM_SIZING` 锁定 16:9，但无边框窗口
   只能用代码/后续自定义手柄改变尺寸；最小/最大尺寸限制仍在。
4. **封面不落盘提取**：有内嵌封面时由 mpv 直接作为视频轨渲染；
   无封面时用渐变 + 首字母瓦片；未实现“导出/缓存封面图片”。
5. **`Ctrl+M` 仍是静音**，未改为迷你模式；迷你模式默认键位是
   `Ctrl+Shift+M`，与规格附录 B 一致。
6. **SMTC 拖动进度**依赖系统是否暴露 `PlaybackPositionChangeRequested`；
   本机未做真实系统拖动验证，仅实现事件接线。
7. **`WM_APPCOMMAND` 媒体键只在窗口聚焦时响应**；全局媒体键由共享 SMTC
   处理。两者都指向活动窗口，不会双处理。
8. **多窗口窗口 frame 持久化是“最后保存者胜出”**：`AppSettings` 只有一组
   `WindowWidth/Height/X/Y`，没有按窗口保存布局；共享设置本身符合要求。
9. **迷你播放列表增高受工作区限制**；屏幕过矮时列表覆盖在窗口内而不是
   完全展开 300 DIP。
10. **未验证 DPI 100/150% 的 PiP 最小尺寸**：本次桌面为 200% 缩放，
    `PipGeometry` 以 DIP 计，Windows 会按 DPI 放大最小尺寸。
11. **截图**：本任务未生成/提交截图；`docs/acceptance/assets/stage-6-*.png`
    由编排会话补录，仅覆盖全屏与 PiP 两个画面。
12. **git 状态**：本任务未执行 commit/push；编排会话已将工作区提交为
    `23bf686 feat(stage-6)` 并推送到 `stage/6-pip`。工作树当前干净。

## 6. 变更文件

新增：

- `src/Wiina.Core/State/ModeStateMachine.cs`
- `src/Wiina.Core/State/PipGeometry.cs`
- `src/Wiina.Core/State/MiniPlayerLayout.cs`
- `src/Wiina.Core/State/WindowCoordinator.cs`
- `src/Wiina.Core/Models/MediaModeAdvice.cs`
- `src/Wiina.App/MainWindow.Modes.cs`
- `src/Wiina.App/PipWindow.xaml(.cs)`
- `src/Wiina.App/Controls/MpvVideoView.xaml(.cs)`
- `src/Wiina.App/Controls/MiniPlayerView.xaml(.cs)`
- `tests/Wiina.Core.Tests/ModeStateMachineTests.cs`
- `tests/Wiina.Core.Tests/PipGeometryTests.cs`
- `tests/Wiina.Core.Tests/MiniPlayerLayoutTests.cs`
- `tests/Wiina.Core.Tests/WindowCoordinatorTests.cs`

修改：

- `src/Wiina.App/App.xaml.cs`（共享设置/SettingsStore、窗口注册表、
  App 级 SMTC 路由、最后窗口退出、新窗口级联）
- `src/Wiina.App/MainWindow.xaml(.cs)`（MpvVideoView、PiP 占位、迷你层、
  F11/ESC 窗口级加速键、模式感知的 chrome/布局/保存/关闭）
- `src/Wiina.App/MainWindow.Playback.cs`（metadata、自动迷你、
  SMTC 改 App 路由、PiP/迷你动作、多窗口菜单/动作、标题同步）
- `src/Wiina.App/Controls/OscToolbar.xaml(.cs)`（PiP 按钮启用）
- `src/Wiina.App/Interop/WindowInterop.cs`（WM_APPCOMMAND + 可选回调）
- `src/Wiina.App/Services/SmtcService.cs`（可选 window、Seek 事件）
- `src/Wiina.App/Themes/Controls.xaml`（`MiniIconButtonStyle`）
- `src/Wiina.Core/Input/KeyMap.cs`（新建窗口/新窗口打开/关闭窗口/Ctrl+P）
- `src/Wiina.Core/Models/AppEnums.cs`（`PlayerMode`、`MediaCommand`）
- `src/Wiina.Core/Models/TrackInfo.cs`（`IsAlbumArt`）
- `src/Wiina.Core/Settings/AppSettings.cs`（自动音乐模式、迷你列表、
  PiP 尺寸）
- `src/Wiina.Mpv/MpvPlayer.cs`（`metadata` 观察/事件、track-list 封面标记）

## 7. 许可证 / 仓库卫生

- [x] 无新增 NuGet/第三方依赖（仅现有 Windows App SDK / xUnit）。
- [x] 未复制 IINA 源码/资产/SF Symbols；XAML/C# 为原创。
- [x] 仓库未提交 libmpv 二进制、测试媒体或截图；临时 WAV/截图仅在 `%TEMP%`。
- [x] 未改变 GPL-3.0-only。
- [x] 已提交并推送到 `stage/6-pip`。

## 8. 截图

### 全屏

![Stage 6 fullscreen](assets/stage-6-fullscreen.png)

### 画中画

![Stage 6 PiP](assets/stage-6-pip.png)

