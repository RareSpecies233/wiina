# Wiina 性能与画质选项：哪些实时生效、以及为什么"性能优先"仍比 PotPlayer 重

- 目标版本：v1.2.7
- 审计快照：分支 `feat/v1.2.7`，HEAD `49bb3d7`
- 证据机器：Windows 11 25H2 (26200)，RTX 3070 Ti / 32.0.16.1088，本次 trace 显示该窗口 `1x`（100% 缩放）
- 性质：**只读分析**，未修改代码、未改设置
- 配套文档：[卡死分析](./hang-analysis-1.2.7.md)、[ADR-0001 视频管线](../architecture/0001-video-pipeline.md)、[既有性能记录](../performance.md)

---

## 0. 摘要（先回答用户的两个问题）

**Q1：画质/性能选项是实时生效，还是要重启？**

**全部实时生效，不需要重启，也不需要重新打开文件。** 81 处 `ApplyMpvString(...)` 调用（统计自 `grep 'ApplyMpvString("'`）把每个设置项直接 `set` 进运行中的 mpv。唯一"启动期固定"的参数是 `vo` / `gpu-api` / `gpu-context` / `d3d11-output-mode`，它们硬编码在 `MpvPlayer.Start()` 里，UI **根本没有暴露**，也不会被重建（全仓库只有 2 处 `new MpvPlayer`：`src/Wiina.App/MainWindow.xaml.cs:1857` 与 `src/Wiina.App/Services/ThumbnailService.cs:473`）。所以"重启才生效"这个担心不存在；反过来，**改这些选项也救不了性能**，因为瓶颈不在它们身上。

**Q2：为什么"性能优先"还是比 PotPlayer 重？**

因为"性能优先"只改了 mpv **GPU 缩放/抖动阶段**的几个参数，而这一层在 1080p 播放里本来就只有每帧零点几毫秒的量级；Wiina 比 PotPlayer 重的那部分完全在别处：

| 税种 | Wiina | PotPlayer | 量级 |
|---|---|---|---|
| 呈现路径 | composition swapchain → XAML 合成器 → DWM | 子 HWND + flip-model（可 DirectFlip/MPO） | 每帧多一次合成 pass |
| DPI 回退 | `SwapChainPanel` 按物理像素布局 + XAML 1/dpi 变换 | 原生处理 | 仅高 DPI 生效（本机 1x 无影响） |
| 毛玻璃取帧 | 8 Hz 全帧 GPU→CPU 回读（1080p ≈ 66 MB/s） | 无此机制 | **纯浪费**，且是卡死放大器 |
| 缩略图 | 常驻第二个 headless mpv | 自带解码线程 | 常驻内存/线程 |
| XAML 叠加 | OSC/标题栏/OSD 全在 XAML 层 | 自绘 | 次要 |

一句话：**"性能优先"调的是画质档位，不是架构开销；用户感觉到的重，来自架构开销。**

---

## 1. 生效性矩阵

图例：**L** = 实时 `set` 生效；**S** = 仅启动时固定（需重建 player = 重启应用）；**D** = 依赖文件/设备重新初始化才生效。

### 1.1 画质预设（23 项，全部 L）

`src/Wiina.Core/Settings/VideoQualityPreset.cs:190-235` 生成 `(key, value)` 列表，`MainWindow.Sidebar.cs:865` 的 `ApplyQualityPreset()` 逐条下发：

| mpv key | 默认档 | 性能优先 | 阶段 |
|---|---|---|---|
| `scale` | lanczos | bilinear | GPU 缩放 |
| `dscale` | hermite | bilinear | GPU 缩放 |
| `cscale` | bilinear | bilinear（**没变**） | 色度缩放 |
| `dither` | fruit | no | 抖动 |
| `dither-depth` | auto | auto | 抖动 |
| `dither-size-fruit` | 6 | 6 | 抖动 |
| `temporal-dither` | no | no | 抖动 |
| `correct-downscaling` | yes | no | 降采样 pass |
| `linear-downscaling` | yes | no | 降采样 pass |
| `sigmoid-upscaling` | yes | no | 升采样 pass |
| `scaler-resizes-only` | yes | yes | 缩放条件 |
| `deband` | no | no（**没变**） | 后处理 |
| `interpolation` | no | no（**没变**） | 插帧 |
| `tscale` | oversample | oversample | 插帧 |
| `video-sync` | audio | audio | 同步 |
| `gpu-dumb-mode` | auto | auto（**没变**） | GPU 管线 |
| `video-sync-max-video-change` | 1.0 | 1.0 | 同步 |
| `deband-iterations/-threshold/-range/-grain` | 1 / 48 / 16 / 32 | 同左 | 后处理（仅在全局去色带开关打开时才下发） |

（`scale-antiring` / `dscale-antiring` / `cscale-antiring` 均 0，两档相同。）

注意 `gpu-dumb-mode` 两档都是 `auto`（`VideoQualityPreset.cs:113`、`:167`）——**"性能优先"并没有启用 dumb mode**，这是对的：dumb mode 会跳过色彩管理/抖动/去色带等整条 GPU 管线，属于"最后手段"，不应作为画质档位的一部分。

**逐项差异（`VideoQualityProfiles.CreatePerformance`，`src/Wiina.Core/Settings/VideoQualityPreset.cs:149-169`）实际只改这 6 个键**：`scale`、`dscale`、`dither`、`correct-downscaling`、`linear-downscaling`、`sigmoid-upscaling`。其余 18 个键与默认档完全相同，`cscale`/`deband`/`interpolation`/`gpu-dumb-mode` 的差异表里标注"没变"就是字面意思。

### 1.2 用户可在 UI 里改、且全部实时生效的项

| 分组 | mpv key | 方式 | 证据 |
|---|---|---|---|
| 视频-硬解 | `hwdec` | L（D：下一次解码器初始化，即换文件/重载后可见） | `MainWindow.Sidebar.cs:629`、`MainWindow.Settings.cs:180-185` |
| 视频-旋转/缩放/平移 | `video-rotate` / `video-zoom` / `video-pan-x` / `video-pan-y` | L | `MainWindow.Settings.cs:210-228` |
| 视频-宽高比 | `video-aspect-override` | L | `MainWindow.Settings.cs:204` |
| 视频-反交错 | `deinterlace` | L | `MainWindow.Settings.cs:201` |
| HDR/色调映射 | `tone-mapping` / `hdr-compute-peak` | L | `MainWindow.Settings.cs:350-351` |
| 音频-设备 | `audio-device` | L | `MainWindow.Settings.cs:245`、`MainWindow.Menu.cs:355-373` |
| 音频-延迟 | `audio-delay` | L | `MainWindow.Settings.cs:242` |
| 音量上限 | `volume-max` | L | `MainWindow.Settings.cs:234` |
| 字幕-字体/大小/位置/颜色/描边/延迟/可见性 | `sub-font` / `sub-font-size` / `sub-pos` / `sub-color` / `sub-border-size` / `sub-delay` / `sub-visibility` / `sub-scale` / `sub-ass-override` / `sub-bold` / `sub-italic` | L | `MainWindow.Settings.cs:253-283` |
| 缓存 | `demuxer-max-bytes` / `demuxer-max-back-bytes` | L | `MainWindow.Settings.cs:286-289` |
| 网络 | `http-proxy` / `user-agent` / `network-timeout` | L（D：对已打开的流需要重新打开） | `MainWindow.Settings.cs:292-298` |

### 1.3 启动期固定（S）：用户改不到，也不需要改

`sre/Wiina.Mpv/MpvPlayer.cs` 的 `Start()` 写入、之后不再改动：

```
vo=gpu-next  gpu-context=d3d11  gpu-api=d3d11
d3d11-output-mode=composition  d3d11-composition-size=<WxH>
force-window=<immediate|no>  idle=yes  keep-open=yes
terminal=no  config=no  input-default-bindings=no  osc=no  osd-level=0
screenshot-format=png  screenshot-directory=…
hwdec=<auto-safe|override>  sub-visibility=yes
```

其中 `d3d11-composition-size` 会随窗口尺寸实时更新（`MainWindow.xaml.cs` 的 `ApplyVideoLayout`），其余为一次性。

**结论：不存在"改了但必须重启才生效"的设置项。** 如果某个设置看起来没生效，优先怀疑 `ApplyMpvString` 的调用路径（例如换文件后是否重新下发），而不是"需要重启"。

---

## 2. "性能优先"到底省了什么

关键在于理解 mpv 的 `scaler-resizes-only=yes`（**两档都开**）：

- 当输出尺寸 == 视频尺寸时，**缩放器根本不运行**。
- 一旦窗口/全屏目标尺寸 != 视频尺寸（例如 trace 里的 1904×1071 vs 1920×1080，或 4K 视频缩到 1080p），缩放器才会跑。

| 场景 | 性能优先省下的工作 | 每帧量级（3070 Ti） |
|---|---|---|
| 1080p 视频 + 1080p 全屏 | 关闭 extra pass（correct/linear/sigmoid），dither fruit→no；缩放器可能不跑 | ≈0.1–0.3 ms |
| 1080p 视频 + 1904×1071 窗口 | 同上 + lanczos→bilinear | ≈0.2–0.5 ms |
| 4K/HEVC 10bit + 1080p 窗口 | 缩放 + 抖动整体变便宜 | ≈0.5–1.5 ms |

参照：60 fps 的预算是 16.7 ms/帧。**即使 4K 降采样，省下的也不到 10%**，而且这些是 GPU 上的可并行工作，通常不是瓶颈。`cscale` 默认已是 `bilinear`（`VideoQualityPreset.cs:108`），`deband`/`interpolation` 默认就是关闭 —— 也就是说"性能优先"真正改到的只有 `scale`/`dscale`/`dither` 和三个布尔 pass。

**所以它有意义（弱 GPU、核显、4K 缩放场景），但在 3070 Ti 上几乎无感 —— 与用户体感一致。**

---

## 3. 为什么仍比 PotPlayer 重：分层账单

### L1 呈现路径（最大的一笔，架构税）

`docs/architecture/0001-video-pipeline.md` 决策 C：mpv 用 `d3d11-output-mode=composition` 把自己的 swapchain 交给 XAML `SwapChainPanel`。

```
PotPlayer：mpv 类似的解码 → 子 HWND 上的 flip-model swapchain → DWM 可直接扫描输出（DirectFlip / MPO）
Wiina    ：mpv → 自己的 swapchain → WinUI 合成器（XAML 层叠加 OSC/标题栏/OSD）→ DWM → 屏幕
```

多出来的一次合成 pass 是**结构性的**：XAML 合成器的输出通常不满足 DWM 的 DirectFlip 条件，于是视频帧无法绕过一次额外拷贝/合成。ADR 选择它的理由是正确的（避免 HWND airspace，XAML 控件必须能浮在视频上）—— 这是拿性能换能力，不是 bug。

### L2 DPI 回退（有条件）

ADR §4.2 实测 `IDXGISwapChain2::SetMatrixTransform` 对 composition swapchain 返回 `DXGI_ERROR_INVALID_CALL 0x887A0001`，于是当前实现是"`SwapChainPanel` 按物理像素布局 + 1/dpi 的 XAML `CompositeTransform`"（`Controls/MpvVideoView.xaml.cs` 的 `SetBufferSize`）。

- 在 100% 缩放（本次 trace 明确 `1x`）下两者等价，无额外开销。
- 在 125%/150% 下，面板被撑到物理像素尺寸再缩放 —— 布局尺寸变大、多一次 composite transform，并在高分辨率屏上放大中间缓冲。

### L3 毛玻璃取帧（纯浪费，且是卡死放大器）

`src/Wiina.App/Interop/ChromeBlur.cs`：

- `FrameIntervalMs = 125`（8 Hz），`TargetPixels = 320`
- 每次调用 `player.ScreenshotRaw()`（`src/Wiina.Mpv/MpvPlayer.cs:514-557`）—— mpv `screenshot-raw`，**同步全帧 GPU→CPU 回读**，文件注释自述"4K 一帧几十 MB"
- 1920×1080×4 B ≈ 8.3 MB/次 → **约 66 MB/s** 的 PCIe 读回，外加每秒 8 次 staging texture 申请/释放
- 拿到的整帧只在 worker 线程上缩小到 320 px 用于毛玻璃

`docs/performance.md` 记录的旧数据（每次 ~45 ms、3.7 MB）与本次 trace 的故障形态（一次截图失败会连带 `AVHWFramesContext: Could not create the staging texture (887a0005)`）互相印证：**这条路径是 Wiina 相对 PotPlayer 多出来的最大单项开销**，而且是唯一会在后台线程持续提交 GPU 工作的东西。

### L4 XAML 层（次要）

- `ChromeBlurLayer.UpdateClip` 在 `LayoutUpdated` 里改自己子元素的 `Width/Height` 并 `new RectangleGeometry`（`ChromeBlur.cs:521-533`）→ 每个布局 pass 一次分配。
- `FadeChrome` 每次淡入淡出都新建一个 `Storyboard`，并给每个属性设 `EnableDependentAnimation = true`（`MainWindow.xaml.cs:769-849`，动画在 `:824` 创建、`:831` 设标志）。旧的 storyboard 现在会先 `Stop()`（`:822`），所以不再有"两个动画打架"的问题；剩下的只是每次 250 ms 一次的分配，以及一个对 `Opacity` 毫无必要的 dependent 标志。
- `SidebarHost` 的 15 ms 动画定时器（`Controls/SidebarHost.xaml.cs:52-55`）。
- 统计面板 1 s 定时器：默认 `ShowMpvStats`/`FrameDropOsd` 关闭时**首行即返回**（`MainWindow.xaml.cs:570-576`），成本可忽略。

### L5 缩略图服务

`src/Wiina.App/Services/ThumbnailService.cs` 常驻一个 **headless mpv**（`:473`），每次取缩略图要 `seek absolute exact` + 20 ms 轮询（最长 2.4 s）+ 3 次截图（`Thread.Sleep(80)`）。平时安静，悬停进度条时是第二个解码器在工作。

### L6 UI 线程动画与布局

窗口 resize 时 80 ms 防抖后重建 pixel 尺寸、clip、composition size（`ApplyVideoLayout`）；拖动中不提交（ADR §4.2 的等价实现）。属正常范围。

---

## 4. 可复现的测量方法（建议 v1.2.7 补上）

现有的 `MainWindow.Stats.cs` 只读 `vo-drop-frame-count`（`MainWindow.xaml.cs:580`）和 `mistimed-frame-count`。**缺少 `frame-drop-count` / `decoder-frame-drop-count` / `vo-delayed-frame-count`** —— 想量化"谁在丢帧"，先把这三个加进统计面板。

| 目标 | 工具/命令 |
|---|---|
| 呈现是否走 flip / 丢帧 | `PresentMon --process_name Wiina.exe --output_file p.csv`（看 `PresentMode` 是否 `Composed`） |
| GPU 占用与合成开销 | `GPUView` 或 `WPR -start GPU` |
| 帧时间 | mpv 统计面板 + 新增的 `frame-drop-count` |
| 毛玻璃成本 | 对照实验：`WIINA_BLUR_DISABLE=1`（`ChromeBlur.cs:131` 已实现）、设置里把毛玻璃切到 Solid、默认 Dynamic |
| 缩放成本 | 同一视频在 100% / 200% 窗口尺寸、窗口化 / 全屏下各测 60 s |
| 硬解成本 | `hwdec=no` vs `auto-safe`（实时 `set`，见 §1.2） |

建议的对照矩阵（每格 60 s，记录丢帧 + GPU 占用）：

```
{blur: Dynamic | Solid | disabled} × {DPI: 100% | 150%} × {hwdec: auto-safe | no} × {窗口 | 全屏}
```

---

## 5. 优化清单（按性价比）

| # | 改动 | 预期收益 | 风险 | 工作量 | 属于 v1.2.7？ |
|---|---|---|---|---|---|
| 1 | `ChromeBlur`：暂停时不抓、间隔 125→250 ms、连续失败退避 | 回读流量 −50~80%，设备异常时不再刷屏 | 低 | 小 | ✅ 与卡死报告 P0-3 同一处改动 |
| 2 | `ChromeBlurLayer`：只在 `SizeChanged` 重建 clip，复用 `RectangleGeometry` | 降 GC 与布局抖动 | 低 | 小 | ✅ |
| 3 | `ApplyVideoLayout`：resize 时复用 geometry（现在 80 ms 一次新建） | 拖动更顺 | 低 | 小 | ✅ |
| 4 | `SwapChainPanel`：仅在 `RasterizationScale != 1` 时走物理像素 hack；并加一次 `SetMatrixTransform` 能力探测 | 高 DPI 下省一次全屏 composite transform | 中 | 中 | ⏳ 可延后 |
| 5 | 缩略图改为一次性预生成 + 磁盘缓存（见 [iina-thumbnails.md](../reference/iina-thumbnails.md)） | 省掉常驻第二个 mpv | 中 | 中 | ⏳ 可延后 |
| 6 | 统计面板补 `frame-drop-count` 等指标 | 让后续优化可量化 | 低 | 小 | ✅ |
| 7 | 呈现路径改 flip-model / render API | 结构性缩小与 PotPlayer 的差距 | 高 | 大 | ❌ 不属于 1.2.7 |
| — | 把"性能优先"做得更激进（如 `gpu-dumb-mode=yes`） | <1% | 画质风险 | 小 | ❌ 不建议 |

**不要做**：继续往"性能优先"里加参数。§2 已经说明这一层的上限不到 10%，而且 `gpu-dumb-mode` 之类的开关会牺牲正确的色彩管理。

---

## 6. 证据索引

| 主题 | 位置 |
|---|---|
| 启动参数固定 | `src/Wiina.Mpv/MpvPlayer.cs` `Start()`（`vo`/`gpu-api`/`gpu-context`/`d3d11-*` 全部在此） |
| 播放器只创建 2 次 | `src/Wiina.App/MainWindow.xaml.cs:1857`、`src/Wiina.App/Services/ThumbnailService.cs:473` |
| 画质预设定义 | `src/Wiina.Core/Settings/VideoQualityPreset.cs:98-235` |
| 画质预设下发 | `src/Wiina.App/MainWindow.Sidebar.cs:865` |
| 全部实时设置调用点 | `grep 'ApplyMpvString("'` → 81 处，`MainWindow.Settings.cs` / `MainWindow.Sidebar.cs` / `MainWindow.Menu.cs` |
| 毛玻璃取帧 | `src/Wiina.App/Interop/ChromeBlur.cs:32`（间隔）、`:205-254`（循环）、`:233`（截图） |
| 同步回读实现 | `src/Wiina.Mpv/MpvPlayer.cs:514-557` |
| DPI 回退与理由 | `docs/architecture/0001-video-pipeline.md` §4.2 |
| 统计定时器 | `src/Wiina.App/MainWindow.xaml.cs:570-598` |
| 既有性能数据 | `docs/performance.md` |

---

## 7. 已知限制

1. 本报告未运行应用、未改设置，量化数字来自代码路径与既有文档（`docs/performance.md`）的量级推算，**不是本机实测**；§4 给出了把推算变成实测的方法。
2. 本机 trace 显示缩放为 `1x`，因此 L2（DPI）在本机不成立，仅对 125%/150% 用户生效。
3. PotPlayer 侧未做同条件实测，对比基于公开的呈现路径差异（子 HWND + flip-model），不是本机 benchmark。
4. `ApplyMpvString` 的 81 处调用点覆盖了 UI 设置，但"某个设置在特定状态下是否被重复下发"（例如换文件后）未逐一验证。
