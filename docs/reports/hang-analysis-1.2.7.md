# Wiina 卡死（程序未响应）分析报告

- 目标版本：v1.2.7（下一个版本）
- 审计快照：分支 `feat/v1.2.7`，HEAD `49bb3d7`，工作区另有 17 项并行未提交改动
- 证据机器：Windows 11 25H2 (26200)，RTX 3070 Ti / 驱动 32.0.16.1088，100%（trace 实测 1x；ADR-0001 Stage 0 的 125% 来自另一台机器/时段）
- 采样时间：2026-10-24 19:20 (+08:00)，事件窗口取近 14 天
- 性质：**只读分析**。本报告未修改任何代码；行号以本快照为准
- 相关文档：[ADR-0001 视频管线](../architecture/0001-video-pipeline.md)、[性能报告](./performance-vs-potplayer.md)

---

## 0. 摘要

近 14 天本机 `Wiina.exe` 的记录分成两个完全不同的事件簇：

| 类别 | 次数 | 版本 | 时间 | 是否有转储 |
|---|---|---|---|---|
| **AppHangB1（程序未响应）** | **2** | **1.2.5.0 / 1.2.6.0** | **10-24 17:55:45 / 19:16:28** | **无** |
| APPCRASH（0xc000027b 等） | 20 | 0.1.0.0 / 1.0.0.0 | 10-18 14:50 ~ 10-19 02:59 | 10 个（38–56 MB） |
| 其中 CoreMessagingXP.dll 0xc000027b | 9 | 0.1.0.0 | 10-18 14:50 ~ 17:04 | 有 |
| 其中 Microsoft.UI.Xaml.dll 0xc000027b @0x3a9dbd | 9 | 0.1.0.0 / 1.0.0.0 | 10-18 18:48 ~ 10-19 02:59 | 有 |
| 其中托管 AccessViolationException（CoreLib） | 1 | 0.1.0.0 | 10-18 16:58:03 | 有 |
| RADAR_PRE_LEAK_64（内存泄漏预警） | 1 | 1.0.0.0 | 10-19 03:30:44 | 无 |

**关键判断：用户报的"程序未响应"就是 10-24 的两次 AppHangB1，且 v1.2.5 / v1.2.6 在 14 天内没有任何崩溃事件**——20 条 APPCRASH 全部来自 10-18/19 的 `0.1.0.0` / `1.0.0.0` 开发与打包验证构建。修复优先级必须把"1.2.6 卡死"放在最前，历史崩溃族单独处理。

19:16 那次留下了完整现场（`%TEMP%\wiina-trace.log`，690 行，含当天 9 个会话），可直接还原：

> 19:14:02 起渲染管线开始异常（libplacebo PTS 警告刷屏），19:14:20 / 19:15:36 / 19:15:59
> 出现三簇 `DXGI_ERROR_DEVICE_REMOVED 0x887A0005`（共 73 次，峰值 8 次/秒）；
> **19:16:01.8763 最后一次截图失败，7 毫秒后（19:16:01.8837）`Window closed` —— 这是 `OnClosed` 的第一行**；
> 此后 trace 再无任何一行，**26 秒后（19:16:28）Windows 判定窗口未响应并关闭进程**。

结论按证据强度排序：

1. **P0-A｜卡死发生在关闭路径（`OnClosed`）内部**（高置信，直接对应 AppHangB1）。`Window closed` 是 `OnClosed` 的入口（`MainWindow.xaml.cs:2264`），窗口要等 `OnClosed` 返回才真正销毁——所以 26 秒的"窗口还在、不响应"正好是 `OnClosed` 阻塞。当天另外 3 次关闭（18:24:45 / 19:05:34 / 19:09:34）都正常，唯一差别是**这次关闭时 GPU 正处于失败状态**（见第 2 条）。候选阻塞点：`_player.Dispose()`（`Join(3s)` + `mpv_terminate_destroy`）与仍在跑的后台截图线程并发；以及 `VideoHost.Detach()` 发生在一个已损坏的 VO 状态上。
2. **P0-B｜GPU 设备移除后没有任何恢复路径**（高置信，解释了从 19:14:02 到关闭这 2 分钟的画面异常）。composition swapchain 绑在 XAML `SwapChainPanel` 上，设备一丢，mpv 无法自行重建；trace 中失败的 2 分钟里**再没有出现新的 `display-swapchain`**，而 Wiina 只在 mpv 重新发布该属性时才重绑。代码里不存在 device-lost 检测/重建（`grep DXGI_ERROR_DEVICE_REMOVED` = 0 命中）。
3. **P0-C｜8 Hz 全帧 GPU→CPU 回读是开销与故障放大器**（高置信）。`ChromeBlur` 每 125 ms 调一次 `mpv screenshot-raw`，每次为硬解帧新建 staging texture 并回读整帧；73 次 `887A0005` 全部来自这条路径，且它是设备丢失后唯一还在持续提交 GPU 工作的线程（关闭时也不停）。
4. **P2｜17:55 那次（v1.2.5）无 trace 覆盖**（trace 从 18:24 才开始），仅有 WER 签名：与 19:16 同为 `AppHangB1`、`P5=134217728` 相同、`P4` 不同（89cf vs 9595），属同一条卡死路径的两个变体。

---

## 1. 证据

### 1.1 事件日志

```powershell
$start = (Get-Date).AddDays(-14)
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=$start; Id=1000,1002,1023,1026} -ErrorAction SilentlyContinue |
  Where-Object { $_.Message -match 'Wiina' } | Sort-Object TimeCreated
```

两次卡死的原始记录（`Application Hang` / Id 1002）：

```
2026-10-24 17:55:45  Id=1002  Application Hang
  程序Wiina.exe版本1.2.5.0停止与 Windows 交互并已关闭。…
  配套 WER 1001 AppHangB1：P1=Wiina.exe P2=1.2.5.0 P3=6ac20000 P4=89cf P5=134217728
2026-10-24 19:16:28  Id=1002  Application Hang
  程序Wiina.exe版本1.2.6.0停止与 Windows 交互并已关闭。…
  配套 WER 1001 AppHangB1：P1=Wiina.exe P2=1.2.6.0 P3=6ac20000 P4=9595 P5=134217728
```

`P5=134217728 (0x08000000)` 两次完全相同，`P4` 是 Windows 为"挂起点"算的特征值，两次不同 → 两次停在不同位置，但同一机制。

### 1.2 WER 与转储

| 位置 | 内容 |
|---|---|
| `%LOCALAPPDATA%\CrashDumps\Wiina.exe.*.dmp` | 10 个，38–56 MB，10-18 18:48 ~ 10-19 02:59 |
| `%ProgramData%\...\WER\ReportArchive\AppCrash_Wiina.exe_*` | 10 个目录（部分 `Report.wer` 读取被拒绝） |
| AppHang 转储 | **不存在**（WER 默认不为 AppHang 生成完整转储 —— 见第 4 节配方） |

10 个转储全部对应 `0.1.0.0`/`1.0.0.0` 构建，**不能代表 1.2.6**；要定位 1.2.7 的卡死必须现场抓取（第 4 节）。

### 1.3 trace 时间线（`%TEMP%\wiina-trace.log`，690 行 / 83,873 字节）

| 时间 | 记录 | 含义 |
|---|---|---|
| 18:24:02.796 | `DBG reset timer shown=True auto=True supp=False sidebar=False flying=0` | 会话 A 首行 |
| 18:24:02.895 | `creating mpv 1904x1071` | composition 尺寸＝窗口物理像素 |
| 18:24:02.954 | `mpv: display-swapchain = 0x26b764fc9f0.` | 绑定成功 |
| 18:24:02.983 | `startup paths (1): …[16bit Sensation Another Layer][01][Ma10p_1080p][x265_flac].mkv` | 1080p HEVC 10-bit |
| 19:14:02.231 | `mpv: vo/gpu-next/libplacebo: Requested PTS 0.000000 is lower than the oldest frame PTS 67.734000…` | 异常起点：截图请求与播放 PTS 打架 |
| 19:14:20.577 | `mpv: ffmpeg: AVHWFramesContext: Could not create the staging texture (887a0005)` | 设备移除第一簇（19:14:20–32，25 次） |
| 19:15:36.026 | 同上（连续 4 秒，每秒 8 次） | 第二簇，峰值 8/s |
| 19:16:01.8763 | 最后一次截图失败 | 距关闭 **7 毫秒** |
| 19:16:01.8837 | `Window closed` | `OnClosed` **入口**（`MainWindow.xaml.cs:2264`） |
| 19:16:28 | `Application Hang / 1002`（v1.2.6.0） | 距上面 **26 秒**，此间 trace 无任何一行 |
| 19:16:35.560 | `VideoHost loaded`（新进程） | 用户重新启动（上一个进程已被 WER 关闭） |
| 19:16:35.682 | `mpv: vo/gpu-next: No frames available to take screenshot of, is a file loaded?` | 首帧尚未出现就发起截图 → blur 循环在启动后 ~100 ms 就开始工作 |
| 19:16:43.424 | `Window closed` | 8 秒后干净退出（无风暴） |

同一份 trace 覆盖当天 9 个会话，其中 4 次关闭：

| 关闭时刻 | 之后发生了什么 | 结论 |
|---|---|---|
| 18:24:45.396 | 4 秒后新会话，无异常 | 正常关闭 |
| 19:05:34.043 | 无异常 | 正常关闭 |
| 19:09:34.506 | 24 秒后新会话 | 正常关闭 |
| **19:16:01.883** | **26 秒无响应后被系统关闭** | **本次卡死** |

前三次关闭时 GPU 都是健康的；唯一一次卡死的关闭，正好发生在设备移除风暴之中。这说明关闭路径不总是阻塞，**触发条件是"关闭时渲染/设备处于异常状态"**。

### 1.4 风暴原文（典型三行一组，随一次截图请求出现）

```text
19:16:00.1143241  mpv: vo/gpu-next/libplacebo: Requested PTS 0.000000 is lower than the oldest frame PTS 67.734000. This is not supported, PTS must be monotonically increasing! Please use `pl_queue_reset` to reset the frame queue on discontinuous PTS jumps.
19:16:00.1148659  mpv: vo/gpu-next: Unknown error occurred while trying to take screenshot!
19:16:00.1152042  mpv: ffmpeg: AVHWFramesContext: Could not create the staging texture (887a0005)
```

统计（19:14:02–19:16:01，每分钟 93 / 125 / 67 行）：

| 文本 | 次数 | 首 / 末 | 分布 |
|---|---|---|---|
| `Could not create the staging texture (887a0005)` | 73 | 19:14:20.577 → 19:16:01.876 | 三簇：19:14:20–32（25 次）、19:15:36–39（32 次，8/s）、19:15:59–19:16:01（14 次） |
| `Unknown error occurred while trying to take screenshot!` | 74 | 19:14:02 → 19:16:01 | 与上一行基本同步 |
| `Requested PTS 0.000000 is lower than the oldest frame PTS 67.734000` | 72 | 19:14:02.231 → 19:16:01.875 | 更均匀，几乎每次截图一条 |
| `screenshot-raw failed: error running command` | 23 | 19:14:… | 中位间隔 630 ms（≈5 个抓帧周期） |

注意：抓帧循环本身是 **125 ms 一次**（8 Hz），但错误只在部分尝试上出现（先卡在 `AVHWFramesContext` 的 73 次，才轮到命令返回失败的 23 次）——所以 73 不是"8 次/秒 × 120 秒"，而是**三簇爆发**；爆发期间确实是 8/s。

### 1.5 这些错误码意味着什么

- `0x887A0005` = `DXGI_ERROR_DEVICE_REMOVED`：GPU 设备被系统移除（TDR 超时、驱动重置、拔显卡/休眠恢复等）。
- `AVHWFramesContext: Could not create the staging texture`：mpv 为了 `screenshot-raw` 要把**硬解（d3d11va）帧**从显存拷回内存，需要新建 staging texture —— 设备已丢，所以每一次都失败。
- `libplacebo: Requested PTS 0.000000 is lower than the oldest frame PTS 67.734000`：会话是从 `resume at 67.734` 恢复播放的；截图请求以 PTS 0 进入渲染队列，与队列里 67.734 的旧帧冲突。**这是 `screenshot-raw` 被高频调用的副作用，不是播放本身的问题。**
- 每帧成本：1920×1080×4 B ≈ 8.3 MB 回读，8 次/秒 ≈ **66 MB/s** 的 PCIe 读回 + 每秒 8 次 staging texture 申请/释放。

### 1.6 机器环境噪声（会放大问题，但不是 Wiina 的 bug）

| 事件 | 数量 | 说明 |
|---|---|---|
| LiveKernelEvent 141/144 | 多条 | 141 属显示驱动 TDR 类事件 |
| BlueScreen 116/133 | 多条 | 历史蓝屏（转储文件名 20251215/20260225/20260405/20260717/20260925） |
| `vmauthd` Application Hang | 216 | VMware 服务，与本问题无关 |
| VMware NAT Service 错误 | 16 | 同上 |

结论：这台机器本来就有 GPU 驱动层面的不稳定史，`887A0005` 出现并不意外；**Wiina 的问题不是"触发了设备丢失"，而是"设备丢失后无法恢复"**。

---

## 2. 根因分析

### 2.1 P0-B：设备移除后没有恢复路径（2 分钟画面异常的主因）

证据链：

| 环节 | 位置 | 事实 |
|---|---|---|
| 唯一持续向 GPU 提交工作的路径 | `src/Wiina.App/Interop/ChromeBlur.cs:233` | `player.ScreenshotRaw()`，`FrameIntervalMs = 125` |
| 同步全帧回读 | `src/Wiina.Mpv/MpvPlayer.cs:514-557` | `mpv_command_node("screenshot-raw")`，文件注释自述"4K 一帧几十 MB" |
| swapchain 只在 **mpv 重新发布**时才重绑 | `src/Wiina.App/MainWindow.xaml.cs:1902-1928` | `OnDisplaySwapChainChanged` → `VideoHost.Attach`；trace 中风暴后**再没有** `display-swapchain = 0x…` 行 |
| 生命周期只覆盖正常关闭 | `docs/architecture/0001-video-pipeline.md` §4.4 | 没有 device-lost 分支 |
| 代码里没有设备丢失处理 | grep `DEVICE_REMOVED` / `device lost` / `D3DERR` | 0 命中 |

机制：device removed → 该 D3D11 设备上所有对象失效 → mpv 的 `vo/gpu-next` 无法继续 Present 到 composition swapchain → XAML `SwapChainPanel` 永远等不到新帧 → 合成器/DWM 侧不再有更新；如果 UI 线程此时还在等 GPU 侧的同步（截图/合成），窗口在 5 秒内就会被 Windows 判为"未响应"。trace 里"风暴持续 2 分钟 → 关闭 → 26 秒静默"与这条路径一致。

**最小修复**（不需要改架构）：

1. 在 `ChromeBlur.Loop`（`ChromeBlur.cs:205-254`）里统计连续失败次数，`>= 3` 时把抓帧间隔退避到 2 s 并 `LogOnce("capture disabled after 3 failures")`；这一条单独就能把"设备丢失后每 125 ms 的无效 GPU 提交"停掉。
2. 在 `MpvPlayer` 增加 `event Action DeviceLost`（由 `ScreenshotRaw` / `GetProperty` 返回的错误码或 mpv 日志中的 `887a0005` 触发），`MainWindow` 收到后执行"重建播放器"：`Pause()` → `VideoHost.Detach()` → `_player.Dispose()` → 走 `OnVideoHostLoaded` 现有创建流程。
3. 在 UI 线程放一个 1 s 心跳（见 §4.6），下一次卡死就能直接判断"是 UI 线程死还是渲染死"。

风险：中（重建播放器会打断播放位置，需要恢复 `resume at` 与播放列表）。

### 2.2 P0-C：8 Hz 全帧回读是纯开销

`ChromeBlur` 的抓帧循环（`Interop/ChromeBlur.cs:205-254`）：

```csharp
MpvPlayer? player = ResolvePlayer();
if (player is { IsRunning: true })
{
    MpvScreenshot? shot = player.ScreenshotRaw();   // 全帧 GPU->CPU
    if (shot is not null && shot.Pixels.Length > 0) Blur(shot);
}
```

- 目的只是给标题栏/OSC 的毛玻璃提供一张 **320 px 宽**的背景图（`TargetPixels = 320`），却每次回读**整帧**再缩小。
- 没有"暂停时不抓"、没有"画面没变就跳过"、没有失败退避。
- `_active` 只在 UI 淡出到隐藏时置 false（`MainWindow.xaml.cs:703`）；鼠标停在窗口上时 OSC 常显，抓帧就常跑。
- 与 `docs/performance.md` 的既有结论一致（"每次 ~45 ms / 3.7 MB"），本报告补充了真实故障形态：设备丢失时它变成错误风暴源头。

**最小修复**：失败退避（见 2.1-1）＋`_mode == Solid` 时本就跳过（已实现）＋暂停时跳过（`_player.GetPropertyFlag("pause")`）＋把间隔从 125 ms 放宽到 250 ms（视觉上几乎无差，GPU 回读减半）。

### 2.3 P0-A：关闭路径竞态（本次卡死的直接位置）

`OnClosed` 尾部（`src/Wiina.App/MainWindow.xaml.cs:2264` 起，实测顺序）：

```
2263  Trace("Window closed")          ← trace 最后一行
…
2294  VideoHost.Detach();             ← 解绑 SwapChainPanel（SetSwapChain(panel, 0)）
2295  _swapChain = 0;
2296  _player?.Dispose();             ← 内部 Join(3s) + mpv_terminate_destroy
```

`MpvPlayer.Dispose`（`src/Wiina.Mpv/MpvPlayer.cs:965-990`）：`_cts.Cancel()` → `_eventThread.Join(3s)` → `mpv_terminate_destroy`。
`ChromeBlur` 线程（`Interop/ChromeBlur.cs:197-202`、`:205-254`）：`IsBackground = true`，**`_running` 永远为 true**，`SetActive(false)` 只是跳过抓帧、不停止线程；`OnClosed` 里**没有任何一处**调用 `ChromeBlur.SetActive(false)`（唯一的 false 在 `MainWindow.xaml.cs:703` 的 UI 淡出路径）。

于是存在窗口：主线程执行 `mpv_terminate_destroy` 的同时，blur 线程可能正在 `mpv_command_node(_handle, …)`。mpv 的客户端 API 是线程安全的，但**与 terminate_destroy 并发调用同一 handle 是明确定义之外的行为**。

三点证据指向这里就是卡死位置：

1. `Window closed` 是 `OnClosed` 的**第一行**，窗口在 `OnClosed` 返回前不会真正销毁 —— 所以 26 秒的"窗口存在且不响应"只能来自它内部。
2. 关闭前 7 毫秒，blur 线程刚在同一个 mpv handle 上失败过一次（`19:16:01.8763` → `19:16:01.8837`）。
3. 当天另外 3 次关闭（GPU 健康时）都在毫秒级完成，没有一次出现同样的静默。

这也解释了为什么"平时关窗没事、偶发卡死"：只有在设备异常 + blur 线程正卡在命令里的时刻关窗，才会撞上。

**最小修复**（低风险，建议 v1.2.7 必做）：

1. `OnClosed` **第一行**就 `ChromeBlur.SetActive(false)`，并新增 `ChromeBlur.Stop()`：`_running = false` + `_thread?.Join(1000)`。
2. 调整顺序：`_player.Pause()` → `ChromeBlur.Stop()` → `VideoHost.Detach()` → `_player.Dispose()`。
3. 把 `SetActive(false)` 从"UI 淡出"语义里拆出来，避免关闭时依赖 UI 状态。

### 2.4 P2：关闭后的进程退出

`App.OnWindowClosed`（`src/Wiina.App/App.xaml.cs` 内，`ShouldExit` 分支）通过 `DispatcherQueue.TryEnqueue(() => app.Exit())` 退出。`Application.Exit()` 在 WinUI 3 + composition swapchain + acrylic 组合下偶有挂起案例，但目前无证据指向它；留作"下一次抓到时优先看"的分支（§4.7 判定树）。

---

### 2.5 排除掉的其它 UI 线程阻塞点（任务要求的逐项排查）

| 候选 | 证据 | 判断 |
|---|---|---|
| UI 线程同步调用 mpv getter | `MainWindow.xaml.cs:570` 的 `OnStatsTick`，`:580` `GetPropertyInt64("vo-drop-frame-count")`，1 s 一次；首行 `:571-576` 在面板隐藏时直接 return | **低风险**。`GetProperty*` 读的是 mpv 自己的属性缓存，不阻塞渲染；且统计面板默认关闭 |
| 设置保存的同步文件 IO | `src/Wiina.Core/Settings/SettingsStore.cs:44-57` `Save()`：`Directory.CreateDirectory` + `File.WriteAllText(temp,…)` + `File.Move` | **低风险**。4 KB JSON，微秒级；由 `_settingsSaveTimer` 防抖后触发。可改为 `Task.Run` 彻底消除 |
| 注册表 / 文件关联 | `src/Wiina.App/Services/FileAssociationService.cs:50-367`，含 `Registry.LocalMachine.CreateSubKey` | **低风险但值得记录**：只在用户点"关联文件类型"时执行，不在播放/关闭路径上；`LocalMachine` 写入在未提权时会抛异常（已被 try/catch 包住） |
| 窗口拖动/resize 每次指针移动都 `AppWindow.Move` | 主窗口拖动走 OS 原生 caption-drag，`WM_MOVING` 只计算吸附；`AppWindow.Move` 出现在 `MainWindow.Playback.cs:1679`、`:2285`、`MainWindow.Modes.cs:635`、`MainWindow.xaml.cs:2213` 等**一次性校正/吸附**路径 | **排除**。没有 per-pointer-move 的窗口移动循环 |
| 缩略图路径在 UI 线程解码 | `src/Wiina.App/Services/ThumbnailService.cs` 的抽取在 `Task.Run` 里，UI 线程只做 `WriteableBitmap` 像素拷贝 | **低风险**（拷贝量随缩略图尺寸，通常 <100 KB） |
| 截图保存 | `src/Wiina.App/MainWindow.Playback.cs:985` `CaptureScreenshot()` → `SendPlayerCommandAsync("screenshot-to-file", …)`，PNG 编码由 mpv 在后台完成 | **排除** |
| XAML 布局循环 | `Interop/ChromeBlur.cs:506-533`：`LayoutUpdated` 回调里改子元素 `Width/Height` 并新建 `RectangleGeometry` | **中风险**（见 P2-2）。在 WinUI 3 里这是 `LayoutCycleException` 的经典诱因，但本次 trace 没有对应证据 |
| 主窗口关闭 | 见 2.3 | **本次卡死的位置** |

结论：本次两次卡死都不能归因于"UI 线程上的同步 mpv 调用 / 文件 IO / 注册表"。UI 线程的证据集中在关闭路径（2.3）。上表里唯一值得在 v1.2.7 顺手修的是 P2-2。

---

## 3. 历史崩溃族（与本次卡死分开处理）

以下全部来自 `0.1.0.0`/`1.0.0.0`（10-18/19 的开发与打包验证），**1.2.5/1.2.6 无一起崩溃**。仍值得修，因为它们在 1.2.7 可能复现。

### 3.1 `Microsoft.UI.Xaml.dll 0xc000027b @0x3a9dbd`（9 次，同一偏移）

`0xc000027b` = `STATUS_STOWED_EXCEPTION`：一个未被观察的 WinRT/COM 异步异常。同一模块同一偏移 9 次 → 可复现的固定路径。

相关代码事实：

- 20 处 `async void`（`grep "async void"`），典型：`MainWindow.Osd.cs`（`RequestThumbnailAsync`）、`MainWindow.Playback.cs`（`OnRootDropAsync`）、`Views/VideoQualityWindow.xaml.cs`（97/120/180/218）、`Controls/KeyBindingsEditor.xaml.cs`（452/504/535/688/722）。
- `App.OnUnhandledException`（`src/Wiina.App/App.xaml.cs:831-836`）**只记日志、不设 `e.Handled`** → 任何 UI 线程异常仍然终止进程。

修复：`async void` → `async Task` + 顶层 try/catch；`UnhandledException` 里对已知可恢复项 `e.Handled = true`；对新抓到的转储执行 `!stowed` 看真实 HRESULT。

### 3.2 托管 `AccessViolationException`（1 次，10-18 16:58:03）

`System.Private.CoreLib.dll 0xc0000005`，栈来自 mpv 节点解析（`.NET Runtime 1026` 记录了未处理异常）。对应代码：`src/Wiina.Mpv/MpvNodeReader.cs` 的 `ReadList` 直接信任 mpv 返回的 `count` 并逐项解引用。若事件线程与 `mpv_terminate_destroy` 竞态（见 2.3），节点内存被释放后解析就会 AV。

修复：`count` 上限校验（如 ≤ 1,000,000）+ 指针非空检查；`Dispose` 先停事件线程再 destroy（现有 `Join(3s)` 已做一半，但 blur 线程未覆盖）。

### 3.3 `CoreMessagingXP.dll 0xc000027b`（9 次，10-18 开发期）

WinAppSDK 的输入/消息循环组件，多发生在窗口销毁/关闭阶段，属于早期构建的退出路径问题；1.2.x 未再出现。若 1.2.7 复现，按 2.3 的关闭顺序先排查。

### 3.4 `RADAR_PRE_LEAK_64`（10-19 03:30）

Windows 的内存泄漏预警。可疑点（本仓库已知的分配热点）：`ChromeBlurLayer.UpdateClip` 每次 `LayoutUpdated` 都 `new RectangleGeometry`（`Interop/ChromeBlur.cs:521-533`）、`FadeChrome` 每次新建 Storyboard（`MainWindow.xaml.cs:769-849`）、缩略图 `WriteableBitmap` 频繁分配。建议 v1.2.7 用 24 h 播放 + `Get-Process` 采样内存曲线确认。

---

## 4. 抓下一次卡死（配方）

当前没有 AppHang 转储 —— 这是"无法定位"的直接原因。按下面四步，下一次卡死就能拿到 UI 线程栈。

### 4.1 常驻 procdump（推荐，最省事）

```powershell
# 下载 Sysinternals procdump64.exe 后，管理员 PowerShell：
procdump64.exe -accepteula -ma -h -e -w Wiina.exe C:\dumps
```

- `-w` 等待进程出现（可以先启动监听再打开 Wiina）
- `-h` 窗口进入"未响应"时抓转储（**这条就是为 AppHang 准备的**）
- `-e` 未处理异常也抓
- 转储体积约 40–60 MB，注意磁盘

### 4.2 或者让 WER 直接为 AppHang 生成转储

```powershell
# 管理员 PowerShell
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Wiina.exe" /v DumpFolder /t REG_EXPAND_SZ /d "%LOCALAPPDATA%\CrashDumps" /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Wiina.exe" /v DumpCount /t REG_DWORD /d 10 /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Wiina.exe" /v DumpType /t REG_DWORD /d 2 /f
```

（`DumpType=2` = 完整转储。AppHang 的转储依赖系统"挂起检测"，命中率不如 procdump `-h`，建议两者同时开。）

### 4.3 卡住时的手动抓取

```powershell
$p = Get-Process Wiina -ErrorAction Stop
procdump64.exe -accepteula -ma $p.Id C:\dumps\wiina-hang.dmp
# 受管侧（.NET 10）也可以：
dotnet-dump collect -p $p.Id -o C:\dumps\wiina-hang.dmp
```

### 4.4 分析（WinDbg / dotnet-dump）

```
# 原生侧：谁卡住了
!analyze -v
~*kb                       # 所有线程栈，找 UI 线程（含 coremessaging / dcompi / ntdll!NtWaitFor*）
!clistack                  # 合成器线程
!stowed                    # 仅 0xc000027b：列出 stowed exception 及 HRESULT
# 托管侧
dotnet-dump analyze C:\dumps\wiina-hang.dmp
clrstack -all
dumpasync                  # 看是否有 await 永远不完成
```

### 4.5 应用侧埋点（v1.2.7 应加入，成本极低）

| 埋点 | 位置 | 目的 |
|---|---|---|
| 心跳 `Trace("hb")` | `DispatcherQueueTimer`，1 s | 卡死时看最后一条 hb 之前发生了什么 |
| `OnClosed` 分步 | `MainWindow.xaml.cs:2264` 起每一步 | 现在只有入口一行，关闭路径完全不可见 |
| `App.Exit` 前后 | `App.xaml.cs` 的 `ShouldExit` 分支 | 区分"关窗前卡"与"退出时卡" |
| blur 失败计数 | `ChromeBlur.cs:244` catch | 直接暴露 device-lost 风暴 |
| mpv 日志文件 | `--log-file` + `--msg-level=all=v`，大小封顶 | 现在只有 `Log` 回调进 trace，级别受限 |

保留手动开关：`WIINA_BLUR_DISABLE=1`（已实现，`ChromeBlur.cs:131`）用于对照实验。

### 4.6 判定树（拿到转储后 30 秒内定位）

| 现象 | 结论 | 去修 |
|---|---|---|
| 最后一条 hb 之前有 `887a0005` 风暴 | 设备丢失（本报告 2.1） | P0-A / P0-B |
| 最后一条 hb 之后无任何记录，转储里 UI 线程在 `mpv_*` / `ntdll!NtWait*` | 关闭路径阻塞（2.3） | P1-A |
| UI 线程在 `Microsoft.UI.Xaml` / `CoreMessagingXP` 等待 | 合成器/输入停止泵消息 | 2.1 的合成器分支 |
| 有托管异常栈 | `0xc000027b` 族（3.1） | 3.1 |

---

## 5. 修复清单

| 优先级 | 问题 | 位置 | 最小修改 | 预期效果 | 风险 | 验证方式 |
|---|---|---|---|---|---|---|
| **P0-1** | 关闭竞态（直接导致本次卡死） | `MainWindow.xaml.cs:2295-2299` | `ChromeBlur.Stop()`（`_running=false` + `Join(1s)`）+ 重排顺序：Pause → blur off → Detach → Dispose | 消除关闭挂起 | 低 | 播放中反复开关窗口 50 次；在 blur 报错时关窗 |
| **P0-2** | 设备丢失后无恢复 | `MainWindow.xaml.cs:1902`、`ChromeBlur.cs:233` | 连续失败 ≥3 退避 + `DeviceLost` 事件 → 重建 player | 设备丢失后自动恢复播放 | 中 | 注入失败/重载驱动后看是否自动恢复 |
| **P0-3** | blur 8 Hz 回读 | `ChromeBlur.cs:248` | 暂停跳过 + 250 ms + 失败退避 | GPU 读回 −50~80% | 低 | 对比 GPU 占用、`docs/performance.md` 数据 |
| **P1-1** | 未处理异常仍终止 | `App.xaml.cs:831-836` | 已知可恢复项 `e.Handled = true` | 减少闪退 | 低 | 注入异常测试 |
| **P1-2** | 20 处 `async void` | 见 3.1 列表 | 改 `async Task` + try/catch | 减少 `0xc000027b` | 中 | 编译 + 手工回归 |
| **P1-3** | `MpvNodeReader` 信任 count | `MpvNodeReader.cs` | 上限校验 + 空指针检查 | 消除托管 AV | 低 | 单元测试 |
| **P1-4** | 关闭路径无埋点 | `OnClosed` | 每步 `Trace` | 下次可定位 | 无 | 直接看 trace |
| **P2-1** | `Trace` 无锁多线程写 | `MainWindow.xaml.cs:132-144` | `lock` + 单写线程（或 `Monitor`） | trace 不再丢行 | 低 | 并发压测 |
| **P2-2** | `LayoutUpdated` 里改布局 | `ChromeBlur.cs:506-533` | 只用 `SizeChanged`，复用 Geometry | 降 CPU/GC | 低 | 帧时间对比 |
| **P2-3** | 版本不可归因 | `Directory.Build.props:7` | 每次发版 `+1`（现为 1.2.6） | WER 可归属 | 无 | 检查 exe 属性 |

---

## 6. 已知限制

1. 两次 AppHang 都**没有转储**，因此 2.1/2.3 的排序基于 trace 与代码，而非线程栈。拿到第 4 节的转储即可定论。
2. 17:55 那次（v1.2.5）无 trace 覆盖（trace 文件从 18:24 开始，可能是每次启动重建/截断）。
3. 10 个现有转储均属 1.0 时代构建，不能用来判断 1.2.6 的崩溃。
4. 部分 `Report.wer` 因权限无法读取（`Access to the path … is denied`），需要管理员权限补采。
5. 本机 GPU 驱动存在历史 TDR 记录，无法排除硬件/驱动侧因素；但"设备丢失后不恢复"是应用侧可修且应修的问题。
