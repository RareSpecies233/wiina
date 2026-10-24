# IINA 缩略图管线剖析 & Wiina 对齐方案

> 目标：回答两个问题——(1) IINA 的时间轴缩略图到底是怎么实现的；(2) Wiina 的预览图为什么"总是加载"（本地与串流都是）。
>
> 基线：`iina/iina`，`develop` 分支，HEAD `5c15f81568b1f61f85219302843018e3c43c8d3b`（2026-10-16）。源码在本工作区 `repo/iina/`。
>
> 证据约定：正文中的 `iina/Xxx.swift:123` 指 `repo/iina/Xxx.swift` 第 123 行；Wiina 的路径均相对仓库根 `C:\Users\zheng\Desktop\wiina`。
>
> **1.2.7 更新**：本文第 3 节的“对齐方案”已在 1.2.7 落地（预生成 + 磁盘缓存）。正文其余部分保留的是
> 1.2.6 时的现状与调研结论，用于对照；实现记录见文末「1.2.7 实现记录」。

---

## 0. 结论速览

一句话：**IINA 是"文件加载后一次性预生成全部缩略图 → 落盘缓存 → 之后每次悬停都是内存里的一次最近邻查表"；Wiina 是"每次悬停（严格说是每个 5 秒桶）现场起一次 headless libmpv 定位、seek、截图"。** 前者在悬停时永远没有"等待"这个状态，后者把提取延迟直接暴露在悬停热路径上，所以"总是加载"。

| 维度 | IINA | Wiina 现状 |
|---|---|---|
| 生成时机 | 文件加载完成时**一次性**预生成 100 张（窗口首次载入时触发） | 悬停时按需，每个 5 s 桶提取一次 |
| 生成者 | 独立 FFmpeg 解码器（`FFmpegController`），与 mpv 播放互不干扰 | 第二个 headless `MpvPlayer` 实例（与播放抢 CPU/GPU） |
| 持久化 | 磁盘缓存（metadata 头 + JPEG 块），重开秒载 | 无磁盘缓存，进程退出即丢 |
| 覆盖范围 | 全片均匀 100 点（2 h 影片 ≈ 每 72 s 一张） | 5 s 一桶（更精细），但只有悬停过的点才有 |
| 查询成本 | 100 条数组线性扫描，微秒级 | 未命中时 = seek + 截图，数百毫秒～秒级 |
| 悬停 UI | `thumbnailsReady` 为真才显示，否则隐藏；**没有 loading 态** | 每次悬停先 `ShowLoading()`，提取完才替换 |
| 流式/网络 | 直接跳过，不生成（macOS 语义下"没有预览"是正常状态） | `File.Exists` 拒绝 → 永远 null，且 UI 无"不可用"态 |
| 尺寸变更 | `thumbnailWidth` 偏好变化 → 清空全部缓存 | 无联动（改设置后旧桶尺寸/精度不一致） |

---

## 1. IINA 的缩略图管线

### 1.1 触发时机：文件加载完成，一次性预生成

两个触发点，语义都是"视频已经就位，可以开始预生成"：

- `iina/MainWindowController.swift:2600`：窗口首次载入视频时 `player.generateThumbnails()`，同时把 `isVideoLoaded` 置真，避免重复触发。
- `iina/PlayerCore.swift:2201-2204`：`fileLoaded` 处理路径里，若主窗口已加载视频，则再调一次 `generateThumbnails()`（用于换片/下一步）。

因此 IINA 的缩略图生成是**与悬停完全解耦**的后台任务；用户看到预览图之前，它已经在后台跑了。

### 1.2 三道前置门

`generateThumbnails()`（`iina/PlayerCore.swift:2907-2949`）先无条件重置状态，再逐道过滤：

```swift
info.thumbnailsReady = false            // :2909
info.$thumbnails.withLock { $0.removeAll(keepingCapacity: true) }   // :2910
info.thumbnailsProgress = 0             // :2911
```

1. **网络资源直接放弃**（`:2915-2918`）：`guard !info.isNetworkResource, let url = info.currentURL else { log("...cannot get file path", .warning); return }`。
   `isNetworkResource` 在 `iina/PlayerCore.swift:2093` 以 `!info.currentURL!.isFileURL` 计算，声明于 `iina/PlaybackInfo.swift:90`。**流媒体在 IINA 里就是没有缩略图**，这是既定产品决策，不是缺陷。
2. **挂载的远程卷**（`:2919-2924`）：除非偏好 `enableThumbnailForRemoteFiles` 为真，否则 `url.resourceValues(forKeys: [.volumeIsLocalKey])` 判定为网络盘时放弃。
3. **偏好开关**（`:2925`）：`enableThumbnailPreview` 为假则什么都不做。

### 1.3 缓存优先：命中就直接 `ready`

`:2926` 用 `info.mpvMd5` 作为缓存名，`ThumbnailCache.fileIsCached(forName:forVideo:)` 校验元数据：

- 命中 → `thumbnailQueue.async` 里 `ThumbnailCache.read(forName:)`，一次性填满 `info.thumbnails`，随后 `thumbnailsReady = true`、`thumbnailsProgress = 1`（`:2928-2940`）。**几百毫秒内悬停即可用，且不做任何解码。**
- 未命中 → `ffmpegController.generateThumbnail(forFile: url.path, thumbWidth: Int32(Preference.integer(for: .thumbnailWidth)) * 2)`（`:2943-2946`）。注意 `* 2`：生成宽度是显示宽度的两倍（Retina 清晰度），显示时再缩。

缓存名 `mpvMd5` 来自 `Utility.mpvWatchLaterMd5(url, ignorePathInWatchLaterConfig)`（`iina/PlayerCore.swift:540`），即"规范化路径的 MD5"，同一文件路径永远得到同一个键。

### 1.4 生成：直连 FFmpeg，均匀抽 100 帧

`iina/FFmpegController.m:82-101` 把任务丢进 `NSBlockOperation`（**先 `[_queue cancelAllOperations]`**，换片时旧任务立刻作废），核心循环在 `getPeeksForFile:thumbnailsWidth:`（`:103` 起）：

| 步骤 | 位置 | 说明 |
|---|---|---|
| 帧数 | `iina/FFmpegController.m:28` `#define THUMB_COUNT_DEFAULT 100` | 固定 100 张，可被 `thumbnailCount` 覆盖 |
| 间隔 | `:204-206` `interval = duration / (double)self.thumbnailCount` | 与时长成正比的均匀采样 |
| 采样点 | `:210-211` `for (i = 0; i <= self.thumbnailCount; i++) { seek_pos = interval * i + start_time; }` | 共 101 次定位 |
| 定位 | `:217` `av_seek_frame(..., seek_pos, AVSEEK_FLAG_BACKWARD)` | 向后关键帧定位，再解码到目标帧 |
| 去重 | `:241-253` 用 `best_effort_timestamp` 建 `_addedTimestamps` | 同一帧被重复解到就跳过 |
| 缩放 | `:198-201` `sws_getContext(..., SWS_BILINEAR)` | 直接在解码器侧缩到 `thumbnailWidth*2` |
| 进度 | `:243-249` 每累计 1 秒才回调一次 `didUpdateThumbnails:...withProgress:` | 避免刷屏 |

输出是内存里的 `FFThumbnail`（`realTime` + `NSImage`），由 `:95-97` 在全部完成后一次性 `didGenerateThumbnails:...succeeded:` 交回。

### 1.5 进度回调与 ready 置位

`PlayerCore: FFmpegControllerDelegate`（`iina/PlayerCore.swift:3218-3247`）：

```swift
func didUpdate(... withProgress progress: Int) {          // :3220
  guard let currentFilePath = info.currentURL?.path, currentFilePath == filename else { return }  // :3221 换片即丢弃
  info.$thumbnails.withLock { $0.append(contentsOf: thumbnails) }   // :3224
  info.thumbnailsProgress = Double(progress) / Double(ffmpegController.thumbnailCount)  // :3226
}
func didGenerate(... succeeded: Bool) {                   // :3230
  if succeeded {
    info.thumbnails = thumbnails; info.thumbnailsReady = true; info.thumbnailsProgress = 1   // :3234-3236
    if let cacheName = info.mpvMd5 { backgroundQueue.async { ThumbnailCache.write(...) } }  // :3240-3243
    events.emit(.thumbnailsReady)                          // :3245
  }
}
```

关键点：**`thumbnailsReady` 只在"全部完成"或"命中磁盘缓存"时置真**；生成过程中（进度 0→99%）悬停是看不到缩略图的，UI 选择"干脆不显示"，而不是"显示个转圈"。

### 1.6 落盘：metadata 头 + JPEG 块

`iina/ThumbnailCache.swift` 是一个自描述的二进制格式，值得逐条对照：

| 片段 | 位置 | 内容 |
|---|---|---|
| 版本 | `:18` `private static let version: CacheVersion = 2` | 协议版本，不匹配即视为失效 |
| 元数据长度 | `:20` `1 + 8 + 8 = 17` 字节 | `version` + `fileSize` + `mtime` |
| 有效性校验 | `:60-64` | 读头，版本/文件大小/修改时间三者全等才认为有效 |
| 图片编码 | `:22-24` `.compressionFactor: 0.75` | JPEG 质量 0.75 |
| 数据块 | `:129-133` | `[Int64 blockLength][Double timestamp][JPEG bytes]` 重复 |
| 写前容量控制 | `:75-80` | `maxThumbnailPreviewCacheSize`（MB）为 0 则不写；超限先 `clearOldCache()` |
| 读损坏即删 | `:166-170` | 块头/图片解不出来 → 删文件返回 nil，下次重新生成 |
| 文件位置 | `:205-207` `Utility.thumbnailCacheURL.appendingPathComponent(name)` | 键即 `mpvMd5` |
| 全清 | `:191-194` `clearThumbnailCache()` | 删目录再重建 |

因为 `fileSize + mtime` 进了头，同一个路径换了内容（重新压制、覆盖）会自动失效，不会串图。

### 1.7 悬停查询：最近时间戳 + `ready` 门控 + 无 loading

`iina/MainWindowController.swift:2302-2311` `refreshSeekTimeAndThumbnail(from:)` 决定"该不该显示"，`updateTimePreviewAndThumbnail(_:)`（`:3016-3054`）负责摆放：

```swift
if player.info.thumbnailsReady, let image = player.info.getThumbnail(forSecond: previewTime.second)?.image {
  thumbnailPeekView.imageView.image = image.rotate(rotation)
  thumbnailPeekView.isHidden = false          // :3031-3033
  ...
} else {
  thumbnailPeekView.isHidden = true            // :3051-3053  ← 不显示是"正常态"，没有 loading
}
```

查找本身（`iina/PlaybackInfo.swift:343-355`）是**向下取整的最近邻**：线性扫描 `$thumbnails`，找到第一个 `realTime >= sec` 的条目，取它的前一个（i==0 时取自己），都不满足则取最后一个。100 条数据，代价可忽略。

`getThumbnail` 结果的尺寸按偏好与显示宽高比重算（`:3041-3049`），并用 `canShowThumbnailAbove(...)`（`:2323-2335`）决定放在进度条上方还是下方（避免出窗被裁）。`ThumbnailPeekView` 自身只是个 4 px 圆角、1 px 描边、带 2 px 阴影的 `NSImageView` 容器（`iina/ThumbnailPeekView.swift:22-42`），没有任何"加载中"视觉。

### 1.8 偏好默认值

`iina/Preference.swift:1126-1129`：

```swift
.enableThumbnailPreview: true,          // 默认开
.maxThumbnailPreviewCacheSize: 500,     // 500 MB 磁盘预算
.enableThumbnailForRemoteFiles: false,  // 网络盘默认不生成
.thumbnailWidth: 120,                   // 显示宽 120 → 生成 240
```

UI 绑定在 `iina/SettingsPageUI.swift:213-222`。**尺寸变更联动**：`iina/AppDelegate.swift:112` 把 `thumbnailWidth` 注册进 `observedPrefKeys`，`:122-123` 观察到变化就 `ThumbnailCache.clearThumbnailCache()`——因为生成宽度变了，旧缓存全部作废。

### 1.9 缩略图的其它消费方

同一份数据被三处复用：OSC 悬停预览、Touch Bar（`iina/TouchBarSupport.swift:366,413`）、Now Playing 信息（`iina/NowPlayingInfoManager.swift:625-640`）。都是"有就显示、没有就跳过"，没有一个会去触发提取。

---

## 2. Wiina 现在的实现

### 2.1 悬停链路

`TimelineControl.PreviewRequested`（挂接于 `src/Wiina.App/MainWindow.xaml.cs:296`）→ `ShowTimePreview(...)`（`MainWindow.xaml.cs:731-781`）：

- `:738-745` 若正在 seek / 侧栏可见 / 拖动 OSC / OSD 盖住时间轴 → `HideTimePreview()` 直接返回。
- `:749-759` 填时间文字与章节名。
- `:761-767` 显示时间卡片、清 Margin、Measure、`PositionTimePreview()`。
- `:769` **唯一的缩略图入口条件**：`if (_settings.ThumbnailPreviewEnabled && File.Exists(_currentPath))`。
- `:771-777` 算尺寸（宽 `Clamp(ThumbnailWidth, 80, 320)`，高按 `_videoAspect` 夹到 45–220），然后 **无条件** `ThumbnailPeek.ShowLoading(时间文本, 宽, 高)`。
- `:779` `RequestThumbnailAsync(seconds)`。
- 收尾：`HideTimePreview()`（`:856`）负责隐藏并释放服务（见 2.4）。

### 2.2 "加载中"是一个显式状态，而且每次悬停都会进入

`ThumbnailPeek.xaml.cs:17-26` `ShowLoading`：`PreviewImage.Source = null`（清空旧图）+ `LoadingRing.IsActive/Visibility = true` + 显示。对应地 `Show(...)`（`:28-37`）才把环停掉、换上位图。

也就是说：**只要悬停，UI 就先把上一张图抹掉、进入转圈态，然后才去要图。** 缓存命中时这条路径在同一帧内走完（`GetAsync` 返回已完成 Task，`await` 同步续行），视觉上不明显；一旦未命中，转圈就是用户看到的全部。

### 2.3 请求调度：有取消，但取消不传播到提取

`src/Wiina.App/MainWindow.Osd.cs:422-488` `RequestThumbnailAsync`：

```csharp
if (!_settings.ThumbnailPreviewEnabled || string.IsNullOrWhiteSpace(_currentPath) ||
    !File.Exists(_currentPath) || _videoWidth <= 0) { Hide(); return; }   // :424-431 第二道 File.Exists 门
long request = ++_thumbnailRequest; _thumbnailCts?.Cancel(); _thumbnailCts = cts;   // :433-436
ThumbnailFrame? frame = await GetThumbnailService().GetAsync(_currentPath, seconds, 90, cts.Token);  // :440-444
if (cts.IsCancellationRequested || request != _thumbnailRequest || !ThumbnailPeek.IsShowing || ...) return;  // :445-451
```

`GetAsync` 的 `cancellationToken` 只在入口检查一次（`src/Wiina.App/Services/ThumbnailService.cs:68`），提取任务是以 `Task.Run(() => ExtractAsync(mediaPath!, key, targetHeight))` 起动的（`:91-92`）——**token 没有传进提取内部**，而且 `_inflight` 里的任务也被后续相同桶的请求复用（`:86-89`）。结论：UI 只是"不要这个结果"，提取照样跑到底并占着那把唯一的 `_extractGate`。

### 2.4 提取：单飞串行，每个桶一次完整"定位 + seek + 截图"

`ExtractAsync`（`ThumbnailService.cs:109-159`）：

1. `await _extractGate.WaitAsync()`（`:116`）——**全局只有一个提取在跑**，`SemaphoreSlim(1,1)`（`:28`）。
2. `seconds = bucket * _bucketSeconds`（`:124`），桶内所有时间点共用一张图。
3. `EnsurePlayer()`（`:161-187`）——复用同一个 headless `MpvPlayer`，未启动则 `Start(1280, 720, headless: true)`。
4. `LoadFileAsync`（`:189-217`）：同一路径直接返回 true；否则 `loadfile replace` 并 `WaitAsync(12s)`（`:202`），失败即整个桶放弃。
5. `SeekAsync`（`:219-238`）：`set pause yes` → `seek <s> absolute exact` → **120 × 20 ms 轮询** `time-pos`/`seeking`，直到误差 < 0.75 s（最坏 2.4 s）。
6. `Capture`（`:240-261`）：`ScreenshotRaw()` 最多试 3 次，每次间隔 `Thread.Sleep(80)`。
7. 失败则 `CaptureToFileAsync`（`:263-291`）：`screenshot-to-file` 落临时 PNG → **80 × 50 ms 轮询文件出现（最坏 4 s）** → `DecodePngAsync` 用 `StorageFile` + `BitmapDecoder` 解回 BGRA（`:293+`）。
8. `Scale`（`:317-409`）手写双线性缩到高 90，`_cache.Put`。
9. `finally { _extractGate.Release(); }`。

单桶最坏耗时粗算：0.2 s（启动，首次）+ 12 s（loadfile 超时）+ 2.4 s（seek）+ 0.24 s（截图重试）+ 4 s（落盘重试）+ 解码 ≈ **秒级以上，典型成功路径也有 0.3–1.5 s**。

### 2.5 缓存：纯内存，60 条，5 秒一桶

- `src/Wiina.Core/Caching/ThumbnailCache.cs:9` 泛型 LRU；`:18` 构造参数 `capacity = 60, bucketSeconds = 5`；键是 `BucketFor(seconds) = floor(seconds / bucketSeconds)`（`:50-63`）；`TryGet` 会把命中项移到队首（`:67-83`）。
- 实例由 `MainWindow.Osd.cs:490-494` 创建：`new ThumbnailService(Clamp(ThumbnailCacheCount,1,240), Clamp(ThumbnailBucketSeconds,1,30), Trace)`，缓存在 `MainWindow.xaml.cs` 的窗口字段上（`_thumbnailService`），仅 `HideTimePreview`（`MainWindow.Osd.cs:548-553`）与设置页"清空缓存"（`MainWindow.Settings.cs:39-52`）会销毁。
- 默认值：`src/Wiina.Core/Settings/AppSettings.cs:58` `ThumbnailPreviewEnabled = true`、`:60` `ThumbnailWidth = 160`、`:62` `ThumbnailBucketSeconds = 5`、`:64` `ThumbnailCacheCount = 60`；范围夹取见 `src/Wiina.Core/Settings/SettingsCatalog.cs:232-254`（80–320 / 1–30 / 10–240）。
- **没有磁盘层**：进程退出、换窗口、清缓存之后，一切都是从零开始。

### 2.6 本地 / 流式的判定

`IsLocalFile`（`ThumbnailService.cs:418-427`）：`File.Exists(path) && Path.GetExtension(path)` 非空。对 `http(s)://` 一律 false，`GetAsync` 在 `:73-76` 直接返回 `null`。

配合上层 `MainWindow.xaml.cs:769` 的 `File.Exists(_currentPath)`，串流场景根本没有进入缩略图链路——`_currentPath` 在 `src/Wiina.App/MainWindow.Playback.cs:1770` 取自 mpv 的 `path` 属性，网络 URL 时就是那个 URL。

### 2.7 headless mpv 的代价

`src/Wiina.Mpv/MpvPlayer.cs:139-207` `Start(..., headless: true)` 的选项（`:156-186`）：`vo=gpu-next`、`gpu-context/gpu-api=d3d11`、`d3d11-output-mode=composition`、`force-window=no`、`idle=yes`、`keep-open=yes`、`paused=yes`、以及 headless 分支的 `audio=no`、`untimed=yes`、`hr-seek=yes`、`video-sync=audio`、**`cache=no`**。

`ScreenshotRaw`（`MpvPlayer.cs:514-557`）是同步 `mpv_command_node("screenshot-raw")`，注释自述"4K 一帧就是要几十 MB"——每次提取都包含一次全分辨率回读再在 C# 侧缩小。

---

## 3. 为什么"总是加载"

### 3.1 本质：Wiina 把"提取"放进了悬停热路径

IINA 的悬停链路只有"查表"和"隐藏"两个分支；Wiina 的悬停链路是"清空旧图 → 转圈 → 发起提取 → 提取好了再换图"。差别不是快慢，是**有没有"已就绪"这个概念**。

### 3.2 本地文件：每移动一点点鼠标，就是一个新桶

- 时间轴的 `PreviewRequested` 每秒会随指针高频触发（`MainWindow.xaml.cs:296` → `:731`）。
- 桶宽 5 s。对一部 2 小时影片、1600 px 宽的时间轴，**每像素 ≈ 4.5 s**——也就是说指针挪 1 px 就跨了一个桶，几乎每个像素都是 cache miss。
- 每个 miss = `ExtractAsync` 完整一轮（seek exact + 截图 + 缩放），典型 0.3–1.5 s，且被 `_extractGate` 串行化。指针扫过 40 个桶，排队的提取就有 40 个，UI 侧 `:445-451` 的 `request != _thumbnailRequest` 会把它们的结果全部丢弃——**算力全花在没人要的帧上，用户看到的永远是上一张（或转圈）**。
- 再加上 60 条容量只覆盖 `60 × 5 s = 5 分钟`素材：对长片，LRU 很快被新桶挤空，回到刚才扫过的区域又要重算。

这就是"总是加载"的主因：**不是第一次慢，而是持续跟不上**。

### 3.3 串流：功能上是"不存在"，不是"慢"

两道 `File.Exists` 门（`MainWindow.xaml.cs:769`、`MainWindow.Osd.cs:427`）加上 `IsLocalFile`（`ThumbnailService.cs:73-76` / `:418-427`）让网络 URL 在第一步就被否决，`GetAsync` 返回 `null`，UI 端只能 `Hide()`（`:453-457`），而 `ThumbnailPeek` 没有任何"不可用/流式不支持"的展示态。IINA 同样不支持网络缩略图（`iina/PlayerCore.swift:2915`），但它**在设置项里就写明"仅本地文件"**，且悬停不会先亮一个转圈。

所以：
- 若看到的串流现象是"没有缩略图、只有时间卡片" → 与代码一致，属既定限制；
- 若看到的是"转圈不停" → 说明该场景 `_currentPath` 指向了一个**真实存在的本地文件**（例如本地 .m3u8/.ts 清单、录制的临时文件），实际走的是 3.2 的慢路径，应当按本地问题处理。

### 3.4 其它放大器

1. **取消不传播**（2.3）：被放弃的提取仍占满唯一并发额度。
2. **首次使用要起一个 mpv 实例**：`EnsurePlayer` 在第一次悬停时 `new MpvPlayer()` + `Start(1280,720,headless:true)`（`ThumbnailService.cs:161-187`），首图额外多几百毫秒。
3. **换文件后 `_loadedPath` 变化**：`LoadFileAsync` 走 `loadfile` + 最长 12 s 等待（`:202`），第一个桶几乎必然吃到这个延迟。
4. **尺寸/设置不同步**：`ThumbnailWidth` 等设置变化不会清缓存（对比 IINA `AppDelegate.swift:122-123`），同一缓存里会混着不同尺寸/不同桶宽的图。
5. **`_videoWidth <= 0` 时直接 `Hide()`**（`MainWindow.Osd.cs:424-431`）—— 文件刚打开的瞬间悬停会闪一下"无缩略图"。

---

## 4. 对齐方案（不实施，仅规格）

### 4.1 目标形态

> 悬停路径 = 纯内存查表；缩略图生成只发生在"文件已加载"之后的后台；第一次生成完成后落盘，之后同一文件秒开；流式/不可用场景有明确状态而不是转圈。

### 4.2 分三步落地

**P0（小改动，先止血）**

1. **悬停不再清空旧图**：`ThumbnailPeek.ShowLoading` 改为"仅当当前没有任何图可显示时才显示转圈"；已有的同一张图保持不变直到新图就绪（`src/Wiina.App/Controls/ThumbnailPeek.xaml.cs:17-26`）。
2. **取消传播**：`GetAsync` 把 `CancellationToken` 传进 `ExtractAsync`，在 `SeekAsync` 轮询与 `CaptureToFileAsync` 轮询里检查；被取消的桶不再占用 `_extractGate`（`ThumbnailService.cs:91-92`、`:228-238`、`:271-278`）。
3. **单飞即最新**：`GetAsync` 只保留"最后一次请求的桶"，排队中的旧桶直接丢弃（在 `_inflight` 之外加一个 `_pendingBucket`）。
4. **预取邻桶**：命中当前桶后，后台顺带提取 ±1 桶，让扫描式移动不再逐格等待。
5. **显式不可用态**：新增 `ThumbnailPeek.ShowUnavailable()`（灰底 + "此来源不生成预览"），流式/远程路径调用它而不是静默 `Hide()`。

**P1（对齐 IINA 的核心：一次性预生成 + 磁盘缓存）**

6. **文件加载完成后预生成一次**：挂在 `PlayerCore` 等价的 file-loaded 事件（Wiina 在 `src/Wiina.App/MainWindow.Playback.cs` 的 `FileLoaded` 处理里），后台按固定步长（建议 `max(桶宽, duration/240)`）顺序提取，写进缓存；UI 用 `ThumbnailsReady` 门控显示，未就绪时保持隐藏（而不是转圈）。
7. **落盘缓存**：新增 `Wiina.Core/Caching/ThumbnailDiskCache.cs`，格式对齐 `ThumbnailCache.swift`——`[version][fileSize][mtime]` 头 + `[len][timestamp][jpeg]` 块；键 = 路径 + 大小 + mtime（无需 `mpvMd5`）；容量上限用设置项（对齐 `maxThumbnailPreviewCacheSize`）。
8. **启动时先读盘**：`GetAsync` 前先 `TryLoadFromDisk`，命中直接 `ready`。
9. **设置联动**：`ThumbnailWidth` / `ThumbnailBucketSeconds` 变化 → 清空内存与磁盘缓存（对齐 `AppDelegate.swift:122-123`）。

**P2（可选，长期）**

10. 用 FFmpeg 直连替代 headless mpv（`FFmpegController` 的等价物）：不抢播放器的 GPU、可并行、能精确控制输出尺寸，与 IINA 完全同构。工作量大，建议放到 P1 验证收益之后再评估。
11. 流式缩略图（IINA 也没做）：若要做，应走"边下边缓存关键帧"或"服务端 API"，而不是本地 seek。

### 4.3 风险

| 风险 | 说明 | 缓解 |
|---|---|---|
| 预生成占用 CPU/IO | 100+ 次 seek 在机械盘/大文件上可能持续数十秒 | 低优先级线程 + 分片让出 + 播放中可暂停 |
| 磁盘缓存损坏/膨胀 | 每片约 100 × 8–20 KB ≈ 1–2 MB | 头部校验 + 读失败即删（照抄 `ThumbnailCache.swift:166-170`）+ 容量上限 |
| ready 门控导致"没有预览" | 长片首次要等 | 预生成可在首帧显示后立即开始；也可先 `ready` 少量（如前 20 张）再增量 |
| 缓存键与文件替换 | 只按路径会串图 | 必须带 size+mtime |

### 4.4 验收建议

- 本地 2 h 影片：第二次悬停开始，任意位置悬停 **≤ 1 帧**出图；首次悬停可接受"暂无预览"但不出现转圈。
- 关闭再打开同一文件：缩略图立即可用（磁盘缓存命中）。
- 快速左右扫动时间轴：无排队堆积（CPU 保持平稳），不出现"越扫越慢"。
- 网络 URL：明确显示"该来源不生成预览"，不出现转圈。

---

## 5. 证据索引

### 5.1 IINA

| 主题 | 位置 |
|---|---|
| 触发（窗口首次载入） | `iina/MainWindowController.swift:2600` |
| 触发（file-loaded） | `iina/PlayerCore.swift:2201-2204` |
| 主流程 / 三道门 / 缓存优先 | `iina/PlayerCore.swift:2907-2949` |
| 生成调用（宽度 ×2） | `iina/PlayerCore.swift:2943-2946` |
| 帧数默认 100 | `iina/FFmpegController.m:28` |
| 任务取消 + 队列 | `iina/FFmpegController.m:82-101` |
| 采样间隔/循环/seek | `iina/FFmpegController.m:204-218` |
| 去重与进度节流 | `iina/FFmpegController.m:241-253` |
| 缩放（sws bilinear） | `iina/FFmpegController.m:198-201` |
| 进度回调 | `iina/PlayerCore.swift:3220-3228` |
| 完成 / ready / 写盘 | `iina/PlayerCore.swift:3230-3247` |
| 缓存格式常量 | `iina/ThumbnailCache.swift:14-24` |
| 有效性校验 | `iina/ThumbnailCache.swift:34-68` |
| 写盘 + 容量控制 | `iina/ThumbnailCache.swift:72-138` |
| 读盘 + 损坏即删 | `iina/ThumbnailCache.swift:142-189` |
| 全清 | `iina/ThumbnailCache.swift:191-194` |
| 悬停显示/隐藏门控 | `iina/MainWindowController.swift:3031-3053` |
| 悬停事件入口 | `iina/MainWindowController.swift:2302-2311` |
| 最近邻查找 | `iina/PlaybackInfo.swift:343-355` |
| peek 视图样式 | `iina/ThumbnailPeekView.swift:22-42` |
| 偏好默认值 | `iina/Preference.swift:1126-1129` |
| 宽度变更清缓存 | `iina/AppDelegate.swift:112,122-123` |
| 其它消费方 | `iina/TouchBarSupport.swift:366,413`；`iina/NowPlayingInfoManager.swift:625-640` |

### 5.2 Wiina

| 主题 | 位置 |
|---|---|
| 悬停入口 | `src/Wiina.App/MainWindow.xaml.cs:296` |
| 悬停显示 + 无条件 loading | `src/Wiina.App/MainWindow.xaml.cs:731-781`（`:769` 门、`:774` ShowLoading、`:779` 请求） |
| 隐藏/收尾 | `src/Wiina.App/MainWindow.xaml.cs:856` |
| loading/show 视觉 | `src/Wiina.App/Controls/ThumbnailPeek.xaml.cs:17-37` |
| 请求调度 + 陈旧结果丢弃 | `src/Wiina.App/MainWindow.Osd.cs:422-488` |
| 服务构造/销毁 | `src/Wiina.App/MainWindow.Osd.cs:490-494`、`:548-553` |
| 清缓存（手动） | `src/Wiina.App/MainWindow.Settings.cs:39-52` |
| 提取主流程 | `src/Wiina.App/Services/ThumbnailService.cs:109-159` |
| 单飞锁 | `src/Wiina.App/Services/ThumbnailService.cs:28,116,151` |
| 内存 LRU | `src/Wiina.Core/Caching/ThumbnailCache.cs:9-83`（`:50-63` 桶键） |
| 去重与 Task.Run（不带 token） | `src/Wiina.App/Services/ThumbnailService.cs:83-106` |
| headless 玩家复用 | `src/Wiina.App/Services/ThumbnailService.cs:161-187` |
| loadfile 12 s 等待 | `src/Wiina.App/Services/ThumbnailService.cs:189-217` |
| seek exact + 轮询 | `src/Wiina.App/Services/ThumbnailService.cs:219-238` |
| 截图重试 | `src/Wiina.App/Services/ThumbnailService.cs:240-261` |
| 落盘截图 + PNG 解码 | `src/Wiina.App/Services/ThumbnailService.cs:263-315` |
| 本地判定 | `src/Wiina.App/Services/ThumbnailService.cs:418-427` |
| 设置默认值 | `src/Wiina.Core/Settings/AppSettings.cs:58-64` |
| 设置范围 | `src/Wiina.Core/Settings/SettingsCatalog.cs:232-254` |
| `_currentPath` 来源 | `src/Wiina.App/MainWindow.Playback.cs:1770` |
| headless mpv 选项 | `src/Wiina.Mpv/MpvPlayer.cs:156-186` |
| `screenshot-raw` | `src/Wiina.Mpv/MpvPlayer.cs:514-557` |

## 1.2.7 实现记录

- **磁盘缓存**：`src/Wiina.Core/Caching/ThumbnailDiskCache.cs`。根目录
  `%LOCALAPPDATA%\Wiina\thumbnails\v2`（`FormatVersion = 2`），每桶一张 JPEG +
  原子重写的 `index.json`（临时文件 + `File.Move` 覆盖），按字节 LRU（`Trim`），
  版本目录自动丢弃旧格式。键 = hash(路径 | 大小 | mtime | 时长 | 桶宽 | 高度)，
  文件大小或 mtime 变化即视为新文件。
- **预生成**：`ThumbnailService.StartPreGeneration`（`src/Wiina.App/Services/ThumbnailService.cs:196`），
  在 `MainWindow.Playback.cs:1730`、`:1790` 媒体加载后触发；单提取 worker，
  预生成高度 90 px、每 5 秒一桶，悬停请求插队。
- **设置**：`ui.thumbnail-cache-mb`（0–4096 MB，步进 64，默认 512，0 = 关闭磁盘缓存）与
  `ui.thumbnail-cache-clear`（清除磁盘 + 内存缓存并提示释放空间）；网络/UNC 源只走内存缓存
  （`ThumbnailService.IsLocalFile` `:903`）。
- **实测**（维护者，1080p 23:40 HEVC）：冷启动预生成 285 桶 14.1–14.9 s，热缓存 0.3 s；
  悬停冷 73 ms / 热 4 ms；285 帧共 1.70 MB。
- **仍存在的差距**：悬停未命中仍要等一次 seek+decode；拖动进度条不做节流；缓存键包含
  mtime/size，原地替换且两者不变时可能命中旧帧。