# 缩略图性能优化可行性（v1.3.7，仅报告）

> 范围：`feat/v1.3.7` 当前树（HEAD `c2dae63`，工作区干净），**只读**调研；本轮不改代码、不构建、不提交。
> 回答用户三问：① 缩放能否前置到 mpv 无头线程？② 即时模式能否用关键帧 seek 加速？③ 慢速模式还能怎么优化？
> 数字来源：`docs/performance.md`（维护者实测）与本文标注的"估算"；所有行号均按本树核对。

---

## 0. 结论先行

| 用户的问题 | 可行性 | 预计收益 | 风险 | 建议 |
| --- | --- | --- | --- | --- |
| ① 缩放前置到 mpv | **可行**（`vf=scale` + 现有 `screenshot-raw`） | 单帧 wall −20~30%（29 → ~20 ms）、CPU −30~45%（15.6 → ~9 ms）；**但省的不是 C# 缩放（<1 ms）**，省的是全分辨率 RGB 转换与 8.3 MB 回读 | 极低；`hwdec` 帧会强制 `hwdownload`（全分辨率下载仍在），需实测两个加速档 | **P1**，先做 E0/E1 确认再改 |
| ② 关键帧 seek | **可行，且只该用在预生成** | 预生成每桶解码段 −30~50%；悬停（即时模式）**不建议**（画面可能早 1 个 GOP） | 精度：最多早一个 GOP；缓存会产生新旧混合（建议 `FormatVersion` 升到 3） | **P1** |
| ③ 慢速模式再优化 | **可行，而且当前设计方向反了** | 现状"慢速模式 = 不预生成"→ 每次悬停都冷启动（73 ms + 队列）。改成"**暂停时中心向外预生成**"后，低配设备可以做到零播放干扰 + 1 s 内可预览 | 中（要接播放器暂停状态） | **P0** |
| ④（额外发现）seek 轮询地板 | 现状每桶至少 `Task.Delay(20)` | 预生成 ~50 ms/桶中约 **20 ms 是轮询地板**（≈40%）；降到 5 ms 或改事件驱动，全片预生成 14.5 s → ~10 s | 无（精度不变） | **P0**，本轮最高性价比 |

一句话：**三个方向都可行，但"缩放前置"和"关键帧 seek"各自只值 20–40%；真正的大头是 20 ms 的 seek 轮询地板，以及慢速模式干脆不预生成。**

---

## 1. 现状与时间预算拆解

### 1.1 已实测的事实

| 项目 | 数值 | 出处 |
| --- | --- | --- |
| 1080p HEVC 单帧（`hwdec=auto-safe`） | **29.0 ms wall / 15.6 ms CPU** | `docs/performance.md:153-154` |
| 1080p HEVC 单帧（`hwdec=no`） | **104.4 ms wall / 460.9 ms CPU** | 同上 |
| 全片预生成（23:40 片子，285 桶） | **14.1–14.9 s 冷**（缓存热 0.3 s） | `docs/performance.md:182-184` |
| 每桶输出 | **90 px 高**、**5 s 桶**、JPEG 约 6 KB | `docs/performance.md:184`；`ThumbnailService.cs:37`（`PreGenerationHeight = 90`） |
| 悬停（冷 / 热） | 73 ms / 4 ms | `docs/performance.md:184` |

由 14.1–14.9 s ÷ 285 桶 ≈ **49.5–52.3 ms/桶**，而单帧解码只占 29 ms → **每桶还有约 21 ms 的"非解码"开销**。

### 1.2 这 21 ms 花在哪（逐项，含估算）

| 环节 | 代码 | 量级 | 说明 |
| --- | --- | --- | --- |
| **seek 轮询地板** | `ThumbnailService.cs:1122-1131`：`for (i<120) { await Task.Delay(20); 读 time-pos/seeking; }` | **≥20 ms，通常正好 20 ms** | 即使 seek 瞬间完成，也要等满一个 20 ms 台阶才检查一次 |
| 解码 + `screenshot-raw` | `:1143` → `MpvPlayer.cs:525` | **29 ms wall / 15.6 ms CPU** | 已实测，占单桶预算约 58% |
| C# 4 抽头缩放 | `:1406-1485`（每输出像素读 4 个源像素） | **0.2–0.5 ms** | 输出只有 160×90，几乎可忽略 |
| JPEG 编码（WIC） | `:1309-1330`（每次新建 `BitmapEncoder`） | **1–3 ms** | 像素量极小，但 COM/WIC 初始化有固定开销 |
| 写盘 + 索引 | `:1253-1280`、`ThumbnailDiskCache.cs:110-126` | **1–3 ms** | 原子 `*.tmp` + `Move`，索引批量 flush |
| 磁盘 miss 探测 | `:746-756` → `:1216-1251`（多一次 `Task.Run` + `FileInfo`） | **0.3–1 ms** | 每桶都做一次 `DescribeSource`（`:1282-1307`） |

> 结论：**预生成的时间预算 ≈ 29 ms 解码 + 20 ms 轮询 + 3–5 ms 后处理**。任何只优化"后处理"的方案上限都只有 ~10%。

### 1.3 数据流（为什么要读回 8.3 MB）

```
headless mpv（vo=gpu-next，无窗口）
  └─ 解码（hwdec=auto-safe → d3d11va）→ GPU 上的 10-bit 帧
       ├─ hwdownload（GPU→CPU，全分辨率 YUV）
       ├─ VO 转 RGB（全分辨率，1920×1080 BGRA ≈ 7.9 MiB）   ← 目前 15.6 ms CPU 的主要来源
       └─ screenshot-raw 返回 { w, h, stride, format, data }
            └─ MpvScreenshotReader.ReadBytes：Marshal.Copy 整帧（8.3 MB）   ← `MpvScreenshot.cs:118-155`
                 └─ Scale()：160×90 4 抽头
                      └─ JPEG → 内存 LRU + 磁盘
```

---

## 2. 问题一：缩放前置到 mpv

### 2.1 先纠正一个直觉

用户以为"在 C# 里缩放 1080p→90px 很贵"。实测不成立：`Scale()` 只处理 **160×90 = 14 400 个输出像素**，每像素 4 次读 + 混合，耗时 **0.2–0.5 ms**（估算，可通过 E1 验证）。

真正贵的是**它前面的两步**：
1. **VO 把 1920×1080 转成 BGRA**（全分辨率色彩转换）→ 15.6 ms CPU 的主体；
2. **把 8.3 MB 拷贝进 `byte[]`**（`MpvScreenshot.cs:129-131`）→ 1–3 ms，外加一次大对象分配（LOH）。

所以"前置缩放"的价值不是省 C# 循环，而是**让上面两步在 160×90 上发生，而不是在 1920×1080 上**。

### 2.2 三个候选方案

| 方案 | 做法 | 可行性 | 评价 |
| --- | --- | --- | --- |
| **A1 `vf=scale`**（推荐） | 在 headless 实例上设 `vf=scale=w=-2:h=90:flags=area`，再 `screenshot-raw` | **可行**：mpv 截图包含滤镜链的输出 | 最小改动（`MpvPlayer.Start` 的 headless 分支加一行，`:185-190`）；`area` 对大比例缩小比现在的 4 抽头**质量更好**（见 2.4） |
| A2 `screenshot-to-file` + `screenshot-format=jpg` | 让 mpv 直接写小图 | 可行但更慢 | 多一次文件写 + 一次 `BitmapDecoder`（`:1190-1212`）；现在它只是失败回退路径（`:1077-1129` 文档口径 / `:1160-1188` 代码） |
| A3 缩小 `d3d11-composition-size` | 把 headless 的渲染目标从 1280×720 改小 | 存疑 | 该选项（`MpvPlayer.cs:160`）只影响渲染目标，**不改变截图尺寸**；可能只省一点点 VO 渲染，需 E0 验证，价值最低 |

### 2.3 `hwdec` 与滤镜的关系（关键可行性问题）

- `hwdec=auto-safe` 时帧在 GPU 上；`vf` 链需要系统内存，mpv 会自动插入 **`hwdownload`**。
- **`hwdownload` 仍然是全分辨率的**（1920×1080 p010 ≈ 6 MB）：这部分省不掉。
- 但 `hwdownload` 之后，`scale` 在 YUV 上做（比 RGB 便宜），**随后 VO 只需把 160×90 转 RGB**。
- 因此"前置缩放"省掉的是：全分辨率 YUV→RGB（约 3–6 ms）+ 8.3 MB 拷贝（1–3 ms）+ 4 抽头（0.3 ms），并让 `screenshot-raw` 只回传 **≈57 KB**。

### 2.4 预期收益（估算，需 E1 实测确认）

| 指标 | 现在 | 前置缩放后（估） | 依据 |
| --- | --- | --- | --- |
| wall / 帧 | 29.0 ms | **18–22 ms** | 剩下的主要是 `hwdownload` + 解码，约 12–16 ms |
| CPU / 帧 | 15.6 ms | **8–10 ms** | 去掉全分辨率 RGB 转换与大块拷贝 |
| 单帧内存分配 | 8.3 MB（LOH） | **57 KB** | `MpvScreenshotReader` 不再拷整帧 |
| 预生成 285 桶 | 14.1–14.9 s | **~11–12 s**（−20%） | 只有解码段变小，20 ms 轮询地板不变 |
| 画面质量 | 4 抽头双线性，**明显锯齿/摩尔纹**（`Scale()` `:1441-1477` 只采 4 点，无面积平均） | **更好**：`flags=area` 做盒式平均 | 这是顺带修复的一个既有质量缺陷 |

> 注意：**若 `hwdec=no`（"仅 CPU"档），收益比例更大**（104 ms 中全分辨率 swscale 占比更高），但绝对收益对大多数用户无意义。

### 2.5 风险与副作用

1. **滤镜可能改变 `hwdec` 决策**：个别驱动上 `vf` 会让 mpv 回退软解 → 必须实测"自动 / 仅 CPU"两档。若发现回退，改在 `vf` 前显式 `--hwdec=d3d11va` 或退回 A1 不启用。
2. **截图尺寸变了**：`MpvScreenshot.Width/Height` 会变成 ~160×90；`Scale()` 仍然要跑（无害，幂等缩放），但**磁盘键用 `height`（`:1282-1307`）不变**，所以旧缓存仍然命中、内容尺寸一致（都是 90 px 高）→ 旧缓存不会错用，只是新旧画质略有差异。
3. **`vf` 会持续生效**：headless 实例是长生命周期的（`:1038-1081`），换片时 `loadfile replace` 不影响 `vf`（正确）。
4. `screenshot-raw` 的 `format` 仍是 BGRA（`MpvScreenshot.cs:103-108`），`Scale()` 的格式分支不受影响。
5. **不能前置到主播放器**（那会改变用户看到的画面）——只允许 headless 实例。

### 2.6 实施点（下轮若决定修）

- `src/Wiina.Mpv/MpvPlayer.cs`：`Start(..., headless)` 的 `if (headless) { … }` 块（`:185-190`）加一行
  `SetOption("vf", $"scale=w=-2:h={height}:flags=area")`，需要 `Start` 接受目标高度参数（或固定 90，与 `PreGenerationHeight` 一致）。
- `src/Wiina.App/Services/ThumbnailService.cs:1070`：把 `PreGenerationHeight`（或 `ClampHeight(targetHeight)`）传进去；`Scale()` 保留为兜底（当返回尺寸与期望不符时）。
- **先做 E0**（§6）：确认当前 `screenshot-raw` 返回的 `w/h` 到底是不是 1920×1080。如果不是（例如已经是渲染目标尺寸），前置缩放的收益接近 0，整项可以取消。

---

## 3. 问题二：即时模式用关键帧 seek

### 3.1 现状

- headless 实例启动时就设了 `hr-seek=yes`（`MpvPlayer.cs:188`）。
- 每次提取：`seek <s> absolute exact`（`ThumbnailService.cs:1116-1120`），然后 20 ms 轮询直到 `!seeking && |time-pos − s| < 0.75`（`:1122-1131`）。
- 也就是说：**当前每一帧都做"精确 seek"= 从最近关键帧解码到目标帧**。

### 3.2 候选方案与收益

| 方案 | mpv 写法 | 预期收益 | 精度 |
| --- | --- | --- | --- |
| **B1 keyframes（预生成专用）** | `seek <s> absolute+keyframes` | 省掉"关键帧 → 目标"的前向解码。长 GOP（动漫 10-bit 常见 1–5 s）时，单桶解码段可降 30–50% | 画面 = 目标时间**之前**的最近关键帧，最多早一个 GOP |
| B2 `hr-seek=no`（headless 全局） | 启动时把 `hr-seek=yes` 改成 `no` | 与 B1 等价但影响所有 seek | 同上 |
| B3 两步 seek | 先 `keyframes` 出图立即返回；若 `time-pos` 偏差 > 桶/2 再补一次 `exact` | 最坏情况回到现状，最好情况省一半 | 可保证精度 |
| B4 `vf=thumbnail` | `--vf=thumbnail` | **不建议**：该滤镜为"播放流里挑代表帧"设计，会缓冲帧、改变输出时机，与"pause + seek + 单帧"模型冲突，延迟不可预测 | — |

### 3.3 精度风险量化

- 桶 5 s（默认，`AppSettings.cs:62`）、GOP 1–5 s：
  - GOP ≤ 5 s → 最多早 1 个桶 → 悬停 7 s 处可能显示 5 s 的画面（用户能察觉）。
  - 因此**只用于预生成**（后台悄悄填缓存），**悬停路径保持 `exact`**。
- 但预生成写入的桶会带"早一个 GOP"的画面 → 之后悬停命中显示同一张（用户会看到"这张图比时间轴晚一点"）。若觉得可见，可把 `video.thumbnail-bucket` 与关键帧对齐（不可控）或退回 B3。

### 3.4 缓存一致性

- 磁盘键 = `SHA-256("wiina-thumbs" + 版本 + 路径 + 大小 + mtime + 时长 + 桶 + 高度)`（`ThumbnailDiskCache.cs:110-126`，版本在 `:117`，`FormatVersion = 2` 在 `:39`）。
- **键里没有"seek 模式"** → 切换后旧缓存（exact 帧）与新缓存（keyframes 帧）混用。
- 建议：切换时把 `FormatVersion` 升到 **3**（`:39`），让旧缓存自然失效（代价：一次性重建，1.70 MB/片的磁盘与 14 s 的 CPU）。这是"要不要视觉一致"的产品决定，本报告把它列为选项而不是默认。

### 3.5 预期收益表

| 指标 | 现在 | B1（仅预生成） | 说明 |
| --- | --- | --- | --- |
| 预生成 285 桶 | 14.1–14.9 s | **~9–11 s** | 只减解码段（29 ms 里的一部分），20 ms 轮询仍在 |
| 即时模式冷悬停 | 73 ms | **不变**（保持 exact） | 精度优先 |
| 若同时做 §5 的 P0-1（轮询 20→5 ms） | — | **~7–8 s** | 两项叠加 |

---

## 4. 问题三：慢速模式还能怎么优化

### 4.1 现状：慢速模式 = **关掉预生成**

`StartPreGeneration` 在慢速模式下直接 `MarkComplete` 并返回（`ThumbnailService.cs:655-661`），`ThumbnailPolicy.AllowsPreGeneration` 也把 `slowMode` 算作不合格（`ThumbnailPolicy.cs:29-37`）。慢速模式剩下的只有：

- 工作线程优先级 `BelowNormal`（`:814-824`）；
- 每帧后 `Thread.Sleep(250)`（`:894-899`，常量 `ThumbnailPolicy.cs:16`）；
- 有用户交互时后台任务再让 120 ms（`:869-873`）。

于是"慢速模式"下**每一次新位置都要冷启动一次 seek+解码**（73 ms + 队列排队），低配设备上表现为"每次悬停都转圈"。**这与用户"希望在低性能设备快乐预览"的目标正好相反。**

### 4.2 单项评估

| 编号 | 措施 | 收益 | 代价 / 风险 | 建议 |
| --- | --- | --- | --- | --- |
| S1 | **中心向外预生成**（从当前播放位置开始向两侧扩展，而不是 0→结尾） | 首个有用缩略图从"14.5 s 后"变成"**1 s 内**"；用户感知收益最大 | 实现需改遍历顺序（`:731` 的 `for bucket = 0..lastBucket`）+ 队列优先级；约 50–100 行 | **P0** |
| S2 | **暂停/无交互时才生成** | 播放时零干扰（低配设备明显）；暂停 3 s 后自动补齐 | 需要读主播放器的 `pause`（可注入一个 `Func<bool>` 或事件）；约 30 行 | **P0** |
| S3 | **悬停邻域预取 ±1 桶** | 拖动时间轴时前后各一桶已就绪，扫动不卡 | 队列多 2 个低优先级任务；约 30 行 | **P0** |
| S4 | **生成步长 ×2**（桶仍是 5 s，隔桶生成，缺失桶回退邻桶） | 预生成任务数减半 | 键/精度语义变化；需 UI 说明；约 40 行 | P1 |
| S5 | **更低分辨率桶**（64 px） | 单帧拷贝 57 KB→32 KB、JPEG 更小；**但解码+下载不变** | 估计总收益 **<5%**；清晰度下降 | P2（仅在内存紧张时有意义） |
| S6 | 更大桶（10 s） | 任务数减半（同 S4） | 会改用户设置；精度下降 | 不建议默认改，交给用户 |
| S7 | IO 优先级/批量 | 286 张 JPEG 才 1.7 MB，机械盘也就几十毫秒 | 复杂、收益小 | 不建议 |
| S8 | 多工作线程并发 | — | GPU 解码串行、内存峰值翻倍、seek 互相抢占 | **不建议** |

### 4.3 推荐的"慢速模式"新定义

> 慢速模式 = **只在暂停时、从当前位置向外、每 2 个桶生成 1 个**，并保持单线程 `BelowNormal` 与 250 ms 冷却；悬停仍然即时可用（`preview-while-generating` 在慢速模式下应默认视为开）。

这样低配设备：播放时零负担；暂停后 1 s 内有邻域预览、几十秒内填满可见区域；不再"每悬停一次冷启动一次"。

### 4.4 与其它设置的相互作用（改慢速模式时必须一起想清楚）

| 设置 | 现状 | 慢速模式改成"暂停时生成"后需要的调整 |
| --- | --- | --- |
| `video.thumbnail-preview-while-generating`（默认关，`SettingsCatalog.cs:267-273`） | 预生成未完成时悬停直接返回 `null`（`ThumbnailService.cs:484-499`） | 慢速模式下应**默认视为开**，否则"暂停才开始生成"会让用户在整个生成期间都没有预览 |
| `video.thumbnail-mode` | 预生成 / 即时（互斥） | 慢速 + 预生成才有意义；即时模式下"暂停时生成"应该仍然不生成（否则等于偷偷升级成预生成） |
| `video.thumbnail-cache-mb`（默认 512 MB） | 磁盘 LRU 按字节 | 不变；慢速模式仍然写盘（生成步长×2 后条目更少） |
| `video.thumbnail-bucket` | 服务构造时读取，改动要重建服务（`MainWindow.Osd.cs:576`） | 生成步长应该是"运行期可变的整数倍"，不要复用桶设置，避免又变成需要重启 |
| `video.thumbnail-acceleration` | 切换时重建 headless player（`:1054`） | 与"暂停时生成"无冲突，但重建那一刻不要和正在运行的预生成抢 |

---

## 5. 路线表（下轮决定是否修）

| 优先级 | 编号 | 内容 | 预计收益 | 成本 | 风险 | 触达文件 |
| --- | --- | --- | --- | --- | --- | --- |
| **P0** | P0-1 | seek 轮询 20 ms → 5 ms（或改观察 `seeking` 属性事件） | 预生成 14.5 s → **~10 s（−30%）**，零精度变化 | **S**（改一个常量/一个循环） | 无 | `ThumbnailService.cs:1122-1131` |
| **P0** | P0-2 | 预生成顺序改"当前位置向外" | 首屏可预览 14.5 s → **<1 s** | M | 并发取消逻辑要顺 | `:719-789` |
| **P0** | P0-3 | 慢速模式改为"暂停时生成 + 中心向外 + 步长×2" | 低配设备播放零干扰、可预览 | M | 需接暂停状态；行为变化需文档 | `:624-678`、`ThumbnailPolicy.cs:29-37` |
| **P0** | P0-4 | 悬停邻域预取 ±1 | 拖动不再"追不上" | S | 队列长度+2 | `:509-547` |
| **P1** | P1-1 | `vf=scale=...:flags=area` 前置缩放 | 单帧 wall −20~30%、CPU −30~45%、质量更好 | S | 需先 E0/E1；hwdec 回退风险 | `MpvPlayer.cs:185-190`、`ThumbnailService.cs:1070` |
| **P1** | P1-2 | 预生成用 `absolute+keyframes`（可选升 `FormatVersion=3`） | 预生成再 −30~50% 解码段 | S | 精度最多早 1 GOP；缓存混用 | `:1116-1120`、`ThumbnailDiskCache.cs:39` |
| **P2** | P2-1 | 64 px 慢速桶 | <5% | S | 清晰度 | `:37`（常量） |
| **P2** | P2-2 | 复用 JPEG 编码器 / 批量编码 | 每桶 −1~2 ms | M | WIC 线程亲和 | `:1309-1330` |
| **P2** | P2-3 | 验证 `d3d11-composition-size` 缩小是否影响截图尺寸 | 未知 | S | 若影响则与 P1-1 重叠 | `MpvPlayer.cs:160` |

### 明确不建议

| 项 | 原因 |
| --- | --- |
| `vf=thumbnail` | 面向"播放流挑代表帧"，与 pause+seek 单帧模型冲突，延迟不可预测 |
| 多 worker 并发 | 同一 GPU 解码器串行；内存峰值翻倍；seek 互相抢占，实测会变慢 |
| 把缩放前置到**主播放器** | 改变用户看到的画面 |
| 用 `screenshot-to-file` 当主路径 | 已有文件写 + PNG 解码双重成本（`:1160-1212`） |
| 去掉缓存键里的 mtime/大小 | 会跨文件版本复用错误画面（IINA 也保留 size+mtime） |
| 默认把桶改成 10 s | 属产品精度取舍，不该由性能优化单方面决定 |
| 在 UI 线程做缩放/编码 | 现状已经不在 UI 线程（`:878` 在专用工作线程），保持即可 |

---

### 5.1 若下轮决定修：建议分三段落地

| 阶段 | 内容 | 验收标准 | 回滚方式 |
| --- | --- | --- | --- |
| 第一段（1 天，零风险） | P0-1 轮询 20→5 ms + P0-4 邻域预取 | 同一文件预生成 ≥25% 提速；拖动时前后一桶已就绪 | 改回常量即可 |
| 第二段（2–3 天） | P0-2 中心向外 + P0-3 慢速模式"暂停时生成 + 步长×2" | 首屏 <1 s 可预览；播放时缩略图相关 CPU 占用 ≈0 | 慢速模式分支保留旧路径，用设置/特性开关切回 |
| 第三段（需实测，1–2 天） | P1-1 前置缩放、P1-2 关键帧 seek | 单帧 wall ≥15%；预生成再 ≥15%；质量主观不下降 | `vf` 清空即回退；seek 标志回 `exact` |

> 建议把第一段单独发一个补丁版本：它只动两处、不改任何可见行为、收益约占本报告总收益的一半。

### 5.2 总收益汇总（估算）

| 场景 | 现状 | 只做 P0 | P0 + P1（全部） |
| --- | --- | --- | --- |
| 全片预生成（285 桶，1080p HEVC，hwdec） | 14.1–14.9 s | **~9–10 s** | **~6–7 s** |
| 单帧 wall / CPU | 29.0 / 15.6 ms | 29.0 / 15.6 ms | **~20 / ~9 ms** |
| 首屏可预览时间 | 14.5 s（等全片跑完） | **<1 s** | <1 s |
| 播放期间缩略图干扰（低配） | 持续存在 | **≈0（慢速模式）** | ≈0 |
| 拖动扫动体验 | 每换位置一次冷启动 | 邻桶命中 | 邻桶命中 + 更快 |
| 画质 | 4 抽头，轻微锯齿 | 不变 | 更好（area） |

---

## 6. 实验方法（本轮未跑长基准）

### 6.1 已经存在的埋点（可直接用，无需改代码）

| 日志行 | 位置 | 用途 |
| --- | --- | --- |
| `thumbnail pre-generation: {extracted} extracted, {reused} cached, {buckets} buckets in {s}s` | `ThumbnailService.cs:785-787` | 全片预生成的总耗时与命中率 |
| `thumbnail {t}s from disk in {ms} ms` | `:561-563` | 磁盘命中耗时 |
| `thumbnail hover delay: {ms} ms` | `:412` | 悬停延迟 |
| `thumbnail hover waiting for pre-generation to finish` | `:490-496` | 预生成闸门 |
| `thumbnail mpv restarting for decode-path change` | `:1054` | 加速档切换 |
| `screenshot-raw failed` / `screenshot-to-file failed` | `:1151` / `:1181` | 回退路径是否被触发 |

日志落在 `%TEMP%\wiina-trace.log`（`MainWindow.Trace`，`MainWindow.xaml.cs:162-175`）。

### 6.2 五个实验

- **E0（先做，10 分钟）**：临时在 `Capture()`（`:1134-1158`）后打一行 `shot.Width x shot.Height`，确认 `screenshot-raw` 到底返回 1920×1080 还是渲染目标尺寸。**这一条决定 P1-1 有没有价值。**
- **E1（单帧分解）**：对同一时间点测 4 段：seek 完成耗时、`ScreenshotRaw` 耗时、`Scale` 耗时、JPEG 耗时（各 20 次取中位数）。用来验证 §1.2 的估算。
- **E2（预生成总时长）**：`hwdec=auto-safe` / `hwdec=no` × `exact` / `keyframes` × 轮询 20 / 5 ms，共 8 组，每组跑 3 次取中位数。判据：P0-1 和 P1-2 是否各自 ≥20%。
- **E3（慢速组合）**：现状慢速 vs "暂停时中心向外"：观察播放时 CPU（任务管理器/PresentMon 无关，本机可看 `Wiina` 进程 CPU%）与"暂停后首次可预览时间"。
- **E4（低端模拟）**：用 VM 或限核（`Process` 亲和性/电源计划）模拟 2 核，验证慢速模式的用户感知改善。

### 6.3 解析脚本（PowerShell）

```powershell
# 全片预生成总时长
Get-Content "$env:TEMP\wiina-trace.log" |
  Select-String 'thumbnail pre-generation:' |
  ForEach-Object { $_.Line } | Select-Object -Last 5

# 磁盘命中耗时分布
Get-Content "$env:TEMP\wiina-trace.log" |
  Select-String 'from disk in (\d+) ms' |
  ForEach-Object { [int]$_.Matches[0].Groups[1].Value } |
  Measure-Object -Average -Maximum -Minimum
```

验收门槛建议：P0-1/P1-2 单组提升 <15% 就回退；P1-1 若 E0 显示截图本就不是源分辨率，直接取消。

---

## 7. 常见误解（都是本轮调研中被排除的候选）

| 误解 | 为什么不行 |
| --- | --- |
| "直接用主播放器截图，省一个 mpv 实例" | 主播放器在播/暂停状态与目标时间不同，截出来不是悬停位置；还会与 ChromeBlur 的 8 Hz 截图互相抢锁（`docs/reports/ui-freeze-analysis.md` 已记录该竞态） |
| "并行 4 个 worker，预生成快 4 倍" | 解码器/GPU 是串行的；`hwdec` 会话通常只能有一个活跃解码；并发只会让每个任务更慢并让内存峰值翻倍 |
| "把桶改成大一点就行了" | 桶是用户可见精度（`video.thumbnail-bucket` 1–30 s），改默认值属产品决策，且要重建服务（`MainWindow.Osd.cs:576`）才生效 |
| "让 mpv 直接输出 JPEG（`screenshot-format=jpg`）就不用编码了" | 该选项只对 `screenshot-to-file` 有效（`MpvPlayer.cs:174`）；文件路径还要再读回来解码（PNG 回退路径 `:1160-1212` 就是这种双重成本），比内存里编 JPEG 慢 |
| "缩放本来就便宜，前置没意义" | 前半句对（0.3 ms），后半句错：前置省的是"全分辨率 RGB 转换 + 8.3 MB 回读"（本节 §2.1），不是缩放本身 |
| "预生成已经在后台，低配设备感觉不到" | 错觉。预生成与播放共用 GPU/CPU，且 `hwdec` 下载占满内存带宽；本仓库自己的性能文档记录整片 14 s 的满负荷 |
| "把 `hwdec=no` 当默认更稳" | 实测 104 ms / 461 ms CPU（`docs/performance.md:154`），比自动档慢 3.5 倍，只有驱动异常时才该用 |

---

## 8. 证据索引

| 主题 | 位置 |
| --- | --- |
| 实测数字（29/15.6、104/461、285 桶 14.1–14.9 s、73/4 ms） | `docs/performance.md:153-154`、`:182-184` |
| 现状说明（架构/设置/缓存/限制） | `docs/reference/thumbnails.md`（§3–§10） |
| headless 启动与选项 | `src/Wiina.Mpv/MpvPlayer.cs:156-194`（`vo` :156、`d3d11-composition-size` :160、`force-window` :163、`screenshot-format` :174、`sub-visibility` :181、headless 块 :185-190） |
| `screenshot-raw` 实现 | `src/Wiina.Mpv/MpvPlayer.cs:525-573`；`src/Wiina.Mpv/MpvScreenshot.cs:118-155` |
| 目标高度常量 | `src/Wiina.App/Services/ThumbnailService.cs:37`（90）、`:1381`（Clamp 24–240） |
| 预生成遍历 | `ThumbnailService.cs:624-678`、`:719-789`（慢速分支 :655-661） |
| 工作线程/优先级/冷却 | `ThumbnailService.cs:832-906`、`:814-824`；`ThumbnailPolicy.cs:16` |
| seek + 轮询 | `ThumbnailService.cs:1113-1132` |
| 截图捕获与回退 | `ThumbnailService.cs:1134-1158`、`:1160-1212` |
| C# 缩放 | `ThumbnailService.cs:1406-1485` |
| JPEG / 磁盘写 | `ThumbnailService.cs:1253-1280`、`:1309-1330` |
| 磁盘键与版本 | `src/Wiina.Core/Caching/ThumbnailDiskCache.cs:39`、`:110-126` |
| 悬停入口 | `src/Wiina.App/MainWindow.Osd.cs:499`、`:576`；`ThumbnailPeek.xaml.cs:17-26` |
| 策略门槛 | `src/Wiina.Core/State/ThumbnailPolicy.cs:29-37`、`:70-77` |

### 已知限制（本报告）

- 所有"预计收益"是基于 29 ms / 50 ms 分解的**估算**，未在本轮实测（用户要求本轮只出报告）。
- E0 未做：`screenshot-raw` 的实际输出尺寸是 P1-1 的前提，必须在实施前用一行日志确认。
- 未验证 mpv 版本对 `vf` + `hwdec` 组合的行为差异（本机 libmpv 与打包版本可能不同）。
