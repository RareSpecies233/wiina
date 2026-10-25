# 高负载下 UI 卡死分析：是 WinUI 3 的锅吗？

> 范围：v1.3.0 / `feat/v1.3.0` 工作树，只读分析，未改任何代码。
> 方法：读代码定路径 + 用上一版跑出来的 trace 定现场 + 给出可复现的测量配方。
> 结论分三档：**卡死（"程序未响应"，秒级）**、**卡顿（掉帧/不跟手，毫秒级）**、**设计成本（不可避免）**。

---

## 0. 一句话结论

**不是 WinUI 3 的锅。**

- WinUI 3 / Composition 给的是**固定成本**：每帧几毫秒的合成器与布局开销。它会让 60 fps 变成 50 fps，但**不会**让窗口"未响应"。
- 真正能把 UI 线程拖到数秒无响应的，只有三类：**跨线程调用正在销毁的 mpv**、**GPU 设备被移除后没有恢复路径**、**UI 线程上的同步阻塞**（目前没有直接阻塞，但有 3 类工作以"每秒几十次"的规模在排队）。
- 让"高负载下特别卡"的，是 4 处**我们自己加上去的**设计：8 Hz 全帧 GPU→CPU 回读、无上限缩略图预生成、每秒 36 次同步 mpv 属性读取、每个布局周期重建 5 份几何。

> 一句话给用户：**WinUI 3 让我们每帧多花几毫秒；卡死是我们自己的截图回读、缩略图预生成和关闭顺序造成的。**

---

## 1. 证据基础

| 证据 | 说明 |
|---|---|
| 本树代码 | 所有结论都有 `文件:行`，见各表 |
| 上一版 trace | `%TEMP%\wiina-trace.log`（v1.2.6 会话，2026-10-24 18:24–19:16）：`screenshot-raw failed` ×23、`887a0005`（DXGI_ERROR_DEVICE_REMOVED）×73、libplacebo `Requested PTS 0.000000 is lower than the oldest frame PTS 67.734000` ×72，全部以 125 ms 周期成串出现 |
| 事件日志 | 2026-10-24 两次 `AppHangB1`；其中 19:16:01.88 已记 `Window closed`，19:16:28 才被 WER 判定挂起 → **挂起发生在关闭之后**，不是播放中 |
| 已有测量 | `docs/performance.md:152-154`：动态毛玻璃 **≈45 ms / 3.7 MB 每 1280×720 帧**，8 fps ≈ ⅓ 个核心 |

**可信度声明**：v1.3.0 没有开启持久日志（`%LOCALAPPDATA%\Wiina` 只有 `settings.json` / `history.json`），所以本文是**代码路径级**结论，不是崩溃栈级结论。§6 给出抓下一次卡死的配方。

---

## 2. UI 线程成本地图（播放中）

| # | 来源 | 触发 / 频率 | UI 线程上做什么 | 证据 |
|---|---|---|---|---|
| 1 | `time-pos` 属性事件 | 每渲染帧 ≈ **24–60 次/秒** | 每个事件一次 `DispatcherQueue.TryEnqueue` → 3 个 `PropertyChanged` → 3 个时间轴控件（底/顶/浮动）各更新 Value + 时间文本 | `MainWindow.xaml.cs:1244-1260`（`case "time-pos"`）、`ViewModels/OscViewModel.cs:296`（`SetPositionFromPlayer`）、`winui-audit` 审计中已记录 3 个 Timeline 绑定同一 VM |
| 2 | 统计面板 | **1 次/秒** | `UpdateStatsPanel()` 在 UI 线程**同步**读 ≈36 个 mpv 属性（`mpv_get_property` 是阻塞调用），再更新 ≈25 个 `TextBlock.Text` | `MainWindow.xaml.cs:570`（`OnStatsTick`）→ `MainWindow.Stats.cs:52-150`；`Controls/PlaybackStatsPanel.xaml.cs:40-59`（只在签名变化时重建，这点做得对） |
| 3 | 毛玻璃截图 | **8 次/秒**（125 ms） | worker 线程：`mpv_command_node("screenshot-raw")` 全帧 GPU→CPU 回读 → CPU 缩放到 320 px → Win2D 高斯模糊；UI 线程：`WriteableBitmap` 写入 230 KB + `Invalidate()` + 5 个层 `Apply()` 各设 5 个变换属性 | `Interop/ChromeBlur.cs:236/242/248`（`Thread.Sleep(FrameIntervalMs)`）、`:257`（`ScreenshotRaw`）、`:309`（320 px 缩放）、`:437-447`（上传+分发）、`:616-620`（每层变换） |
| 4 | 毛玻璃层几何 | **每个布局周期 × 5 层** | `UpdateClip()` 写 `_fill.Width/Height`（布局属性）并 **new 一个 RectangleGeometry** 赋给 `Clip` | `Interop/ChromeBlur.cs:531`（`LayoutUpdated += UpdateClip`）、`:545-557`；挂载点 5 处：`TitleBarView.xaml.cs:129`、`OscBottomView.xaml.cs:43`、`OscFloatingView.xaml.cs:51`、`OsdText.xaml.cs:250`、`SidebarHost.xaml.cs:95` |
| 5 | 缩略图预生成 | 打开文件后**连续跑完整个视频** | 后台 worker：每 5 s 一个桶 → seek + 解码 + `screenshot-raw`（**第二个 mpv 实例**）→ JPEG 编码 → 写盘；UI 线程只在命中时更新 | `MainWindow.Osd.cs:505-532`（`StartThumbnailPreGeneration`）、`Services/ThumbnailService.cs:196-318`（`PreGenerateAsync` 遍历 `0..duration/bucket`）、`:35`（高度 90）、`AppSettings.cs:62`（桶 5 s）、`:67`（默认 512 MB） |
| 6 | 悬停缩略图 | 指针在时间轴上移动 | 每次移动 `ShowLoading()` + 取消上一次请求 + 建新 token；命中后建 `WriteableBitmap` 写流 | `MainWindow.Osd.cs:490-560`（`RequestThumbnailAsync` 区域） |
| 7 | chrome 淡入淡出 | 指针进出 chrome | 每个元素一个 `DoubleAnimation`，`EnableDependentAnimation = true` → 依赖动画在 UI 线程逐帧 tick | `MainWindow.xaml.cs:824-846`（`Duration 250 ms`，最多 4 元素）；同样的写法：`PipWindow.xaml.cs:215`、`MiniPlayerView.xaml.cs:268`、`OsdText.xaml.cs:141/164`、`SettingsWindow.xaml.cs:271/280` |
| 8 | 设置保存 | 去抖后 | UI 线程**同步** `File.WriteAllText`（JSON 约 4 KB） | `MainWindow.xaml.cs:2396-2433`（`SaveSettings`）→ `src/Wiina.Core/Settings/SettingsStore.cs:44-53` |
| 9 | 窗口缩放 | 拖动时 80 ms 去抖 | `ApplyVideoLayout()`：重算像素尺寸、`SetBufferSize`、设 `d3d11-composition-size`、**新建 RectangleGeometry 作为 Clip** | `MainWindow.xaml.cs`（`_resizeTimer` 间隔 80 ms，`:55`/`:160-174`）、`ApplyVideoLayout` 的 `new RectangleGeometry` |
| 10 | 侧边栏动画 | 打开/关闭动画期间 15 ms | 手动 tick 的缓动 | `Controls/SidebarHost.xaml.cs:37`（`_animTimer` 15 ms） |

**合计（播放中、chrome 可见、开了统计面板）**：约 **50–90 个 DispatcherQueue 工作项/秒**，其中 8 次带 GPU 回读、1 次带 36 次阻塞属性读。这个量级本身**吃不满** UI 线程；问题在它们**撞在一起**时（4K 硬解 + 回读 + 预生成）产生的长尾。

---

## 3. 哪些是 WinUI 3 / Composition 的**固有**成本

| 成本 | 性质 | 量级（量级估算，非实测） | 我们的选择 | 证据 |
|---|---|---|---|---|
| composition 模式的交换链（无 flip） | 每帧由 XAML 合成器再交给 DWM 合成，**没有独立翻转** | 每帧 1–3 ms 合成器时间 + 一次额外拷贝 | 为了 XAML 覆盖层（OSC/字幕/UI）必须这么做 | `docs/architecture/0001-video-pipeline.md`（决策：`d3d11-output-mode=composition`，拒绝 `--wid` 子窗口） |
| `SwapChainPanel` 按物理像素布局 + `1/dpi` 的 XAML 变换 | 合成器要多做一次缩放 + 裁剪 | 高 DPI 下多 2–4 ms/帧 | `SetMatrixTransform` 在本机返回 `DXGI_ERROR_INVALID_CALL`，只能这样绕 | `Interop/SwapChainPanelInterop.cs`（文件头注释）、`Controls/MpvVideoView.xaml.cs`（`SetBufferSize` 设物理尺寸 + `1/scale`） |
| 单一 UI 线程 + 60 Hz tick | 布局/绑定/依赖动画都在一个线程 | 一次 100 ms 阻塞 = 掉 6 帧 | 这是 WinUI 3 的模型，不是缺陷 | — |
| `ListView` 虚拟化 | 正常（`ItemsStackPanel` 默认虚拟化） | 无 | 播放列表/章节都用 `ListView` + `ObservableCollection` | `Controls/PlaylistView.xaml:56`、`ChaptersView.xaml:23` |
| ContentDialog / Flyout 在 popup 层 | 独立视觉树，`ApplyToTree` 够不到 | 无 | 与卡死无关 | 本会话早前审计已记录 |

**这部分结论**：固有成本合计约 **3–8 ms/帧**（含合成器与 1/dpi 缩放）。它解释"为什么同样的解码在 PotPlayer 里更省"，**不解释"为什么窗口未响应"**。

---

## 4. 能造成"程序未响应"的候选（按可能性排序）

### R1（P0，最可能）关闭路径上仍在并发调用 mpv

```csharp
// MainWindow.xaml.cs:2467-2477（现状）
ChromeBlur.SetActive(false);
ChromeBlur.Shutdown();      // 内部 thread.Join(1 s)
WindowInterop.Detach(this);
VideoHost.Detach();
_swapChain = 0;
_player?.Dispose();         // mpv_terminate_destroy + 事件线程 Join(3 s)
```

- `ChromeBlur.Shutdown()` 只 `Join(TimeSpan.FromSeconds(1))`（`Interop/ChromeBlur.cs:83-100`），**超时后继续执行**。
- 而 worker 里 `player.ScreenshotRaw()`（`ChromeBlur.cs:257`）是**同步** `mpv_command_node`，GPU 设备移除/驱动卡住时它可以阻塞数秒。
- 于是 `_player.Dispose()` 可能在 worker 仍在 `mpv_command_node` 时执行 `mpv_terminate_destroy` → 这正是文件注释里承认过的竞态（`:2467-2468` "used to race disposal"），**现在只是被 1 秒超时"缓解"，没有消除**。
- 与事件日志吻合：19:16:01.88 记 `Window closed`，19:16:28 才被判 `AppHangB1` → **卡在关闭里**。

**最小修复**：`Shutdown()` 返回 `bool`（是否真正 join 成功）；失败时**不调用** `_player.Dispose()`，改为把 `MpvPlayer` 交给后台线程延迟销毁（或直接 `Environment.Exit` 兜底）；同时给 worker 一个 `CancellationToken` 并在 `ScreenshotRaw` 前检查。

### R2（P0，环境相关）GPU 设备移除（TDR）后无恢复路径

trace 里 `887a0005` = `DXGI_ERROR_DEVICE_REMOVED` 出现 73 次，且本机有大量 `LiveKernelEvent 141/144`、`BlueScreen 116/133` 历史（2025-12–2026-09 的 dump 名）。设备移除后：

- mpv 的 `vo=gpu-next` 会持续报错，`screenshot-raw` 全部失败（trace 中每 125 ms 一次的风暴就是它）；
- 我们没有监听 `device-removed`、没有降级、没有提示，UI 仍在每 125 ms 重试。

**最小修复**：mpv 有 `vo/gpu-next` 的 `gpu-context` 失败日志；检测到连续 N 次截图失败 → 退避 30 s；监听 mpv 的 `GPU context lost` 日志/`--gpu-dumb-mode` 降级；给用户一个"渲染器已重置，建议重启"的 OSD。

### R3（P1）崩溃类历史（不是卡死，但同一批事件里）

- `Microsoft.UI.Xaml.dll 3.2.3.0` 的 `0xc000027b`（stowed exception）×3、`0xc0000005` ×1（2026-10-19）；
- `System.AccessViolationException` 在 `MpvNodeValue.ReadList / MpvNodeValue.Read / MpvPlayer.HandleNodeProperty / EventLoop`（2026-10-18）。

**修复**：`OnUnhandledException` 只写日志、**不设 `e.Handled`**，且 `MpvNodeReader` 对 mpv 返回的 node 只做 `Clamp(count, 0, 10000)`，没有对 `Keys`/`Values` 指针做校验。建议：节点读取加 try/catch 边界 + 事件线程异常不穿透（`EventLoop` 外层 catch 已有一处，`HandleNodeProperty` 内部 catch 也有，但 AV 属于 `AccessViolationException`，在 .NET Core 里默认**不可捕获**——只能靠指针校验避免）。

### R4（P2）UI 线程同步写磁盘

`SettingsStore.Save` 是同步 `File.WriteAllText`（`SettingsStore.cs:53`），在预生成正在写缩略图磁盘缓存时，一次保存可能几十 ms。量级不大，但属于"可以在 UI 线程上消失"的工作。

---

## 5. 造成"高负载下卡顿"的可避免设计（按影响排序）

### D1（P0）缩略图预生成无上限，且"慢速模式"是死设置

- 打开任意本地文件即在后台**遍历整段视频**（`ThumbnailService.cs:263-302`，`lastBucket = duration / 5 s`）。2 小时片子 = **1440 次 seek + 解码 + `screenshot-raw`**，与主播放器**抢同一块 GPU**。
- 设置里有 `ThumbnailSlowMode`（`AppSettings.cs:74`）、设置页有开关（`SettingsCatalog.cs:273`）和一段"遇到卡顿就打开慢速模式"的提示文案（`SettingsCatalog.cs:276-278`）——**但没有任何代码读这个字段**（全仓 `grep SlowMode` 只有这 3 处声明/绑定）。
- 这是最坏的一种：用户按提示关掉，什么也没变。

**最小修复**：①预生成限制在"当前播放点 ±90 s"的窗口内滚动，而不是全片；②把 `ThumbnailSlowMode` 真正接进 worker（限制 1 个桶/2 s + 低线程优先级）；③播放中（未暂停）默认暂停预生成。

### D2（P1）毛玻璃 8 Hz 全帧回读

- 每 125 ms 一次 `screenshot-raw`，仓库自己测过 **≈45 ms / 3.7 MB 每 1280×720 帧**（`docs/performance.md:153`）→ **≈⅓ 个核心持续占用**，且它是**同步**命令，会占住 mpv 的核心锁，与解码/渲染抢时间。
- 暂停时也在抓（`_active` 只看 chrome 是否可见，`ChromeBlur.cs:216-226`），静止画面完全不需要。
- 失败时没有退避（trace 里 73 次连续失败）。

**最小修复**：①`_active` 增加"未暂停 或 首次需要一帧"条件（读 `pause` 属性即可）；②`FrameIntervalMs` 125 → 250（4 Hz 肉眼几乎无差别，成本减半）；③连续失败 3 次 → 退避 30 s；④`ChromeBlurMode.Fixed`（`固定`）已经是"每次显示只抓一帧"，把默认值改成它收益最大。

### D3（P1）统计面板每秒 36 次**同步** mpv 属性读

- `UpdateStatsPanel()` 全在 UI 线程（`OnStatsTick` 是 `DispatcherQueueTimer`，`:570`），每次 `GetPropertyString/Int64/Double` → `mpv_get_property`（阻塞）。
- 这些属性里 `video-params/*`、`estimated-vf-fps`、`display-fps` 会让 mpv 现算，重负载下单次可达毫秒级；36 次叠加在 seek/换轨瞬间可能上到几十 ms。

**最小修复**：把读取整体挪到线程池（`Task.Run` 读 → 拿快照 → `DispatcherQueue` 只做 `SetRows`），或至少把只变不常变的字段（编码/色彩/GPU）缓存，只刷新随时间变的（时间/丢帧/avsync/缓冲）。

### D4（P1）`ChromeBlurLayer.UpdateClip` 挂在 `LayoutUpdated`

- 每次布局周期（窗口里任何元素尺寸变化都会触发，5 个层各一次）都 `new RectangleGeometry`（`ChromeBlur.cs:545-557`）。
- 而 `time-pos` 每秒 24–60 次更新 `Slider.Value`，可以间接引起布局 → **几何分配与合成器重建被放大到每秒数百次**。
- 同时它写 `_fill.Width/Height`（布局属性）——在布局回调里改布局属性，是 WinUI 里典型的抖动来源。

**最小修复**：删掉 `LayoutUpdated` 订阅，只在 `SizeChanged` 里 `UpdateClip()`；复用同一个 `RectangleGeometry`，改 `Rect` 而不是 new。

### D5（P1）`time-pos` 每个事件都进 DispatcherQueue

- 60 fps 源 = 60 个工作项/秒 ×（Position + CurrentTimeText + ProgressRatio）× 3 个时间轴控件。队列无合并：UI 卡 200 ms 后，会一次性涌入 12 个过期位置更新。

**最小修复**：在 `OnNumberPropertyChanged` 里对 `time-pos` 做 100 ms 合并（记录最新值 + 一个 `bool _positionPending`，只在空闲时投递一次）。

### D6（P1）`EnableDependentAnimation = true` 用在 `Opacity` 上

- `Opacity` 是**独立动画**属性，本来可以交给合成器；加了 `EnableDependentAnimation = true` 反而强制它在 UI 线程逐帧跑（`MainWindow.xaml.cs:831` 等 7 处）。每次 chrome 淡入淡出 = 250 ms 的 UI 线程逐帧回调。

**最小修复**：删掉这 7 处 `EnableDependentAnimation = true`。

### D7（P2）其他

- `ApplyVideoLayout` 每次 resize tick 新建 `RectangleGeometry`（见 §2 #9）。
- `OnStringPropertyChanged` 对 `media-title` **投递两次** `TryEnqueue`（`MainWindow.xaml.cs:1311-1321`）。
- 5 个 `ChromeBlurLayer` 每帧各设 4 个 `CompositeTransform` 属性（`ChromeBlur.cs:616-620`），可合并为每帧一次。

---

## 6. 如何证明（10 分钟实验）

### 6.1 对照实验矩阵（不改代码）

| 组 | 操作 | 预期 |
|---|---|---|
| A 基线 | 4K HEVC 10-bit，开统计 + 动态毛玻璃 + 缩略图预生成 | 参考组 |
| B 关毛玻璃 | 环境变量 `WIINA_BLUR_DISABLE=1`（`ChromeBlur.cs:125` 附近 `TryAttach` 直接返回 false）或设置里改"固定" | 若 CPU 占用明显下降 → D2 成立 |
| C 关预生成 | 设置 → 缩略图磁盘缓存 0 MB（`SetDiskCacheLimit(0)` 会 `StopPreGeneration`，`ThumbnailService.cs:244-252`） | 若打开文件后的卡顿消失 → D1 成立 |
| D 关统计 | `ShowMpvStats = false` | 若周期性 1 s 尖峰消失 → D3 成立 |

### 6.2 工具

```powershell
# UI 线程热点（WPA 看 Microsoft.UI.Xaml.dll 与 DispatcherQueue）
wpr -start CPU -filemode
# …复现卡顿/卡死…
wpr -stop ui.etl
# 帧呈现（是否掉帧、是否 flip）
PresentMon.exe --process_name Wiina.exe --output_file present.csv
# 卡死现场（进程还在但无响应时）
procdump -ma -h Wiina.exe C:\dump\wiina-hang.dmp   # 需要 procdump
# 或者注册 LocalDumps 让所有崩溃自动落盘
```

### 6.3 建议加的内部埋点（比任何工具都直接）

1. **DispatcherQueue 排队深度**：每 500 ms 记一次 `DispatcherQueue` 待处理项数（无公开 API，可近似：在 tick 里记 `Environment.TickCount64` 与上一次 tick 的差）。
2. **每个计时器的实际间隔**：`_statsTimer` / chrome blur 的 `ApplyPending` 各记一次实际耗时，>50 ms 就写 trace。
3. **`mpv_get_property` 累计耗时**：在 `UpdateStatsPanel` 前后读 `Stopwatch`，写一行 trace。
4. **关闭路径分阶段 trace**：`OnClosed` 已有一个 `Trace("Window closed")`；在 `ChromeBlur.Shutdown()`、`Detach`、`Dispose` 前后各加一行 + 耗时，下次挂起就能一眼定位（这正是这次 19:16 会话缺的信息）。

---

## 7. 优先级汇总

| 优先级 | 项 | 文件:行 | 影响 | 成本 |
|---|---|---|---|---|
| P0 | 关闭路径：join 失败仍销毁 mpv | `MainWindow.xaml.cs:2467-2477`、`Interop/ChromeBlur.cs:83-100` | 消除"关闭时未响应" | 小 |
| P0 | 预生成改为窗口化 + 播放中暂停 | `Services/ThumbnailService.cs:257-318`、`MainWindow.Osd.cs:505-532` | 消除打开文件后的 CPU/GPU 抢占 | 中 |
| P0 | 让 `ThumbnailSlowMode` 真正生效 | `AppSettings.cs:74` ↔ `ThumbnailService.cs`（当前无读者） | 兑现设置页承诺 | 小 |
| P0 | 设备移除后的退避与提示 | `Interop/ChromeBlur.cs:257`、`MpvPlayer` 日志 | 消除 8 Hz 失败风暴 | 中 |
| P1 | 统计面板读属性移到后台 | `MainWindow.Stats.cs:52-150` | 消除每秒 36 次阻塞 | 小 |
| P1 | 删掉 `LayoutUpdated` 的几何重建 | `Interop/ChromeBlur.cs:531,545-557` | 减少每布局周期分配 | 小 |
| P1 | `time-pos` 合并到 10 Hz | `MainWindow.xaml.cs:1244-1260` | 减少 60→10 项/s | 小 |
| P1 | 去掉 7 处 `EnableDependentAnimation` | 见 §2 #7 | 淡入淡出不再占 UI 线程 | 小 |
| P2 | 设置保存移到后台 | `SettingsStore.cs:44-53` | 偶发阻塞 | 小 |
| P2 | 复用 `RectangleGeometry`、合并变换写入 | `ApplyVideoLayout`、`ChromeBlur.cs:616-620` | 微优化 | 小 |

---

## 8. 证据索引

| 主题 | 位置 |
|---|---|
| time-pos 编组 | `src/Wiina.App/MainWindow.xaml.cs:1244-1260`；`src/Wiina.App/ViewModels/OscViewModel.cs:296` |
| 统计面板 36 次同步读 | `src/Wiina.App/MainWindow.Stats.cs:52-150`；`src/Wiina.App/MainWindow.xaml.cs:570` |
| 毛玻璃捕获循环 | `src/Wiina.App/Interop/ChromeBlur.cs:31-33,68-100,236-275,309,437-447,616-620` |
| 毛玻璃层几何 | `src/Wiina.App/Interop/ChromeBlur.cs:497-557` |
| 毛玻璃挂载点 | `TitleBarView.xaml.cs:129`、`OscBottomView.xaml.cs:43`、`OscFloatingView.xaml.cs:51`、`OsdText.xaml.cs:250`、`SidebarHost.xaml.cs:95` |
| 预生成 | `src/Wiina.App/Services/ThumbnailService.cs:196-318`；`src/Wiina.App/MainWindow.Osd.cs:505-532` |
| 慢速模式死设置 | `src/Wiina.Core/Settings/AppSettings.cs:74`、`SettingsCatalog.cs:273-278` |
| 关闭顺序 | `src/Wiina.App/MainWindow.xaml.cs:2435-2478` |
| 依赖动画 | `MainWindow.xaml.cs:824-846`、`PipWindow.xaml.cs:215`、`MiniPlayerView.xaml.cs:268`、`OsdText.xaml.cs:141/164`、`SettingsWindow.xaml.cs:271/280` |
| 同步写设置 | `src/Wiina.Core/Settings/SettingsStore.cs:44-53`；`MainWindow.xaml.cs:2396-2433` |
| 固有合成成本 | `docs/architecture/0001-video-pipeline.md`；`src/Wiina.App/Interop/SwapChainPanelInterop.cs`；`src/Wiina.App/Controls/MpvVideoView.xaml.cs` |
| 已有测量 | `docs/performance.md:152-154` |
