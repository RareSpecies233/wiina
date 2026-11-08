# 缩略图 v1.3.8 改造方案（慢速模式重构 / 关键帧开关 / 即时模式 UX / macOS 差距）——1.3.9 实施记录

> 仓库 `C:\Users\zheng\Desktop\wiina`，原始方案分支 `feat/v1.3.8`（HEAD `1d8ab4d`）。**本文最初是只读方案**；**1.3.9 已实施 A/B/C**（慢速模式重构、关键帧 seek 开关、即时模式 UX），实施差异与未做项见 §7。D（macOS 差距）仍是分析，E 的测试方案部分落地。
> 依据：v1.3.7 报告 `docs/reports/thumbnails-optimization-1.3.7.md` + 本树逐行核对（行号按 `feat/v1.3.8` 当前树）。
> 上一轮的三项缩放方案（A1 `vf=scale` / A2 `screenshot-to-file` / A3 `composition-size`）**按用户判断全部不做**，理由记录在 §0.2。

---

## 0. 决策摘要

### 0.1 本轮要落地的四件事

| 编号 | 改动 | 一句话 | 用户可见变化 |
| --- | --- | --- | --- |
| **A** | 慢速模式重构 | 慢速 = 强制即时预览 + 禁止预生成 + 设置界面锁死冲突项 + 关键帧 seek + 单线程最低优先级 | 打开慢速模式后，界面里"生成模式"被锁为"即时预览"，不再出现"显示预生成但实际不预生成"的错位 |
| **B** | 关键帧 seek 开关 | 新设置行 `video.thumbnail-keyframe-seek`，默认**关**；只作用于预生成/慢速路径，悬停保 exact | 打开后整片缩略图生成更快，个别帧可能比时间点早一个关键帧间隔 |
| **C** | 即时模式 UX | 去掉转圈；首帧之前显示黑底；已有一帧时加载期间**保留上一帧** | 扫动时间轴不再闪白/转圈，画面连续 |
| **D** | 解释差距 | 为什么 macOS M1 Pro 15 s、本机 Windows 30–50 s | 给出可量化的预算模型 + 验证方法（不是"macOS 更快"这种废话） |

### 0.2 明确不做（来自用户最新意图）

| 项 | 结论 | 依据 |
| --- | --- | --- |
| A1 `vf=scale` 预缩放 | **不做** | 滤镜链会强制 `hwdownload` 走软件路径，等于把解码从 GPU 拉回 CPU；收益（8.3 MB 回读）抵不上失去硬解的代价。v1.3.7 报告里已把该项标为"需 E0 实测"且风险中等，用户的判断与实测风险一致 |
| A2 `screenshot-to-file` + jpg | **不做** | 多一次写盘 + 一次 `BitmapDecoder`（`ThumbnailService.cs:1160-1212`），比内存里编 JPEG 更慢 |
| A3 缩小 `d3d11-composition-size` | **不做** | 该选项只改渲染目标（`MpvPlayer.cs:160`），与截图尺寸无关，收益接近 0 |
| 多 worker 并发 | **不做** | 同一 GPU 解码器串行，内存峰值翻倍 |
| `vf=thumbnail` | **不做** | 面向"播放流挑代表帧"，与 pause+seek 单帧模型冲突 |

---

## 1. A：慢速模式重构

### 1.1 现状：三套语义互相打架（这就是"逻辑乱"的根因）

| 现象 | 代码位置 | 后果 |
| --- | --- | --- |
| 慢速模式**根本不预生成** | `ThumbnailService.cs:655-661`：`if (_slowMode) { _pregen.MarkComplete(mediaPath); return; }` | 打开后整片遍历永远不会发生 |
| 但设置里的"生成模式"**仍显示"预生成（推荐）"** | `SettingsCatalog.cs:249-256` 与 `:296-302` 是两个**互相独立**的行，没有任何联动 | 用户以为在预生成，实际每次悬停都在现解码 |
| 慢速模式下 `_instantMode` 仍是 false | `_instantMode` 只由 `Mode` 的 setter 维护（`:360-393`），与慢速无关 | **悬停延迟（hover delay）在慢速模式下完全不生效**（`:513-514` 只在 `_instantMode` 时读延迟） |
| 非即时模式下不会丢弃排队中的悬停任务 | `CancelPendingHoverJobsLocked()` 只在即时分支调用（`:539`，另见 `:642`） | 慢速模式下拖动指针会把沿途**每一个经过的桶**都排进队列；队列串行 + 每帧 250 ms 冷却（`:894-899`），最新位置排在队尾 → 预览严重滞后 |
| 慢速模式仍写磁盘 | 文档口径 `docs/reference/thumbnails.md` §11、`ThumbnailService.cs:1253-1280` | 行为本身没问题，但如果强行切到即时模式，磁盘缓存会**一起消失**（见 §1.3 的 D1） |

> 一句话：**慢速模式现在既不是"慢速预生成"，也不是"即时预览"，而是"没有预生成的即时预览 + 不生效的悬停延迟 + 不做丢弃的队列"**。用户说"逻辑乱"是准确的。

### 1.2 目标语义（按用户意图）

```
慢速模式 = 单线程低优先级 + 关键帧 seek + 即时预览（不做全片遍历）+ 设置锁定
```

四条硬规则：

1. **有效模式恒为即时预览**（与持久化的 `settings.ThumbnailMode` 解耦）。
2. **禁止预生成**：任何进入慢速模式的路径都不启动遍历（现有 `:655-661` 保留，作为第二道闸）。
3. **设置界面锁定**：把与慢速模式冲突的行置灰并显示原因，避免"显示预生成实际不生成"。
4. **单线程最低优先级**（不是"单核"，原因见 §1.5）。

### 1.3 实现方案

#### D1（关键设计决定）：不要改写用户的 `ThumbnailMode`

- **错误做法**：打开慢速模式时把 `settings.ThumbnailMode = Instant` 写进设置。关闭慢速后用户原先选的"预生成"就丢了。
- **正确做法**：新增**有效模式**概念，服务内部用 `_effectiveMode = _slowMode ? ThumbnailMode.Instant : _settingsMode`；持久化字段只由"缩略图生成模式"这一行写入。
- 设置行显示也要跟着变：`ChoiceRow` 的 getter 改成 `settings => (int)(settings.ThumbnailSlowMode ? ThumbnailMode.Instant : settings.ThumbnailMode)`（`SettingsCatalog.cs:253` 一行），这样锁定时显示"即时预览"而不是骗人的"预生成"。

#### D2（关键设计决定）：强制即时 **不等于** 关闭磁盘缓存

- 现状：`ThumbnailPolicy.AllowsDiskCache(Mode)` 对 `Instant` 返回 false（`ThumbnailPolicy.cs:44-45`），因此"强制即时"会**顺带废掉磁盘缓存**——低配设备本来最该用的就是磁盘缓存。
- 建议：把策略改成 `AllowsDiskCache(mode, slowMode)`，**慢速模式仍允许读/写磁盘**，只是不预生成。
- 收益：低端设备第二次打开同一文件时，悬停直接从磁盘读（免解码）；代价：慢速模式仍会写盘（今天也写，行为不变）。

#### D3：设置界面锁定（"锁 UI"）

现状 `SettingsRowDefinition` 只有**静态**的 `UnavailableReason`（`SettingsModels.cs:95-104`）；`SettingsWindow.xaml.cs:485-496` 按它做 0.55 透明 + `IsEnabled=false`。但它是在 `SettingsCatalog.BuildRows()`（`SettingsCatalog.cs:32,58`）里一次性构建的，**运行期无法变化**。

状态提醒：调研期间 `src/Wiina.App/Views/SettingsWindow.xaml.cs` 与 `src/Wiina.Core/Settings/` 正在被其他 agent 修改（`git status` 可见），D3 落地前先与那边对齐，避免重复改同一段渲染代码。

三个可选实现（按推荐度排序）：

| 方案 | 做法 | 成本 | 评价 |
| --- | --- | --- | --- |
| **① 新增 `Func<AppSettings,bool>? AvailableWhen`** | 行定义加一个谓词；`SettingsWindow` 渲染时求值，并在开关变化后重建当前页 | 小（Core 模型 +10 行，UI +5 行） | **推荐**：语义清晰，将来别的行也能用 |
| ② 仅在 SettingsWindow 里按 id 硬编码 | 渲染后对 `video.thumbnail-mode` / `video.thumbnail-preview-while-generating` 特判 | 最小（只动 UI） | 可行，但把业务规则漏进视图层，且搜索/键盘路径仍可能改到 |
| ③ 每次重建整个 catalog | 把 settings 传进 `BuildRows()` | 大 | 不做：`RowList` 是 `static readonly`（`SettingsCatalog.cs:32`），改造成本与风险都高 |

**锁定范围（慢速模式开启时）**：

| 设置行 | 锁定？ | 原因 |
| --- | --- | --- |
| `video.thumbnail-mode` | **锁**，值显示"即时预览" | 慢速模式恒为即时，显示与行为一致 |
| `video.thumbnail-preview-while-generating` | **锁**（置灰为关） | 没有全片遍历，这个闸门无意义（`ThumbnailPolicy.cs:70-77`） |
| `video.thumbnail-bucket` | 不锁 | 仍决定内存/磁盘的桶粒度 |
| `video.thumbnail-cache-mb` | 不锁 | D2 决定慢速仍用磁盘 |
| `video.thumbnail-hover-delay` | 不锁（保持默认 250 ms） | 慢速模式下终于会生效（现状 bug 一并修好） |
| `video.thumbnail-acceleration` | 不锁 | 与模式无关 |
| `video.thumbnail-slow` 自身 | 不锁 | 用户要能关掉它 |

#### D4：代码落点

| 文件 | 改动 |
| --- | --- |
| `src/Wiina.App/Services/ThumbnailService.cs:172-203` | `SlowMode` setter 内联动：进入时 `_instantMode = true` + `CancelPendingHoverJobsLocked()` + `StopPreGeneration()`；退出时恢复传入的持久化模式。新增 `SetEffectiveMode` 或在 `Mode` getter 里返回有效值 |
| `src/Wiina.App/Services/ThumbnailService.cs:360-393` | `Mode` 改为"有效模式"语义；`SetMode` 只记录持久化模式 |
| `src/Wiina.App/MainWindow.Settings.cs:162-203` | 把 `video.thumbnail-mode` 与 `video.thumbnail-slow` 两个 case 合并为一个 `ApplyThumbnailMode()`，避免两条独立路径互相覆盖（顺带修 `:191` 那行被吃掉换行的注释） |
| `src/Wiina.Core/Settings/SettingsCatalog.cs:249-256`、`:296-302` | 模式行 getter 改为有效值；`video.thumbnail-slow` 的描述改成准确表述（"强制即时预览 + 单线程低优先级，不做全片预生成"） |
| `src/Wiina.Core/Settings/SettingsModels.cs:95-104`、`src/Wiina.App/Views/SettingsWindow.xaml.cs:485-496` | 新增 `AvailableWhen` 谓词与渲染时求值 |
| `src/Wiina.App/MainWindow.Osd.cs:499-539` | 慢速模式下也要在拖动时丢弃排队中的悬停任务（复用即时分支） |

### 1.4 强制关键帧 seek（慢速路径）

- 现状：headless 启动即 `hr-seek=yes`（`MpvPlayer.cs:188`），每次提取 `seek <t> absolute exact`（`ThumbnailService.cs:1116-1120`）。
- 慢速模式：`SeekAsync(..., keyframes: true)` → `seek <t> absolute+keyframes`。
- **精度**：画面落在目标时间之前最近的关键帧，最多早一个 GOP。动漫 10-bit 常见 1–5 s；桶默认 5 s（`AppSettings.cs:62`）→ 最坏早约一个桶。
- 与 B 的关系：B 是"用户开关"，A 是"慢速模式强制"。建议**慢速模式无条件开启关键帧**（它是性能优先档），B 只管普通预生成路径。

### 1.5 "单核"要诚实：只能约束我们自己的线程

- `workers` 线程是我们起的（`ThumbnailService.cs:832-836`），可以用 `Thread.Priority = Lowest` + `SetThreadIdealProcessor()` / `SetThreadAffinityMask()` 限制。
- **但 libmpv 的解码线程是 mpv 自己起的**（进程内，见 `MpvPlayer.cs:200-205` 的 event 线程与 mpv 内部的 decoder/filter 线程），线程亲和性**管不到它们**。
- 进程级亲和性（`SetProcessAffinityMask`）会同时限制播放器和 UI，**不可接受**。
- 因此文案必须是"**单线程、低优先级**"，不能写"只用 1 个 CPU 核心"。真实收益来自：不预生成（省掉整段后台负载）+ 关键帧 seek（省掉 GOP 前向解码）+ 250 ms 冷却 + 最低优先级。
- 风险：混合架构 CPU（P/E 核）上 `SetThreadIdealProcessor` 可能落在 E-core；Windows 10/11 都支持，但属"提示"语义，不保证。
- 结论：**建议只做"Lowest 优先级 + IdealProcessor 提示"，不做强制亲和性**（强制亲和性在 12 代以后反而更慢）。这条也写进设置行说明，避免用户误解。

### 1.6 预期收益

| 场景 | 现状（慢速模式） | 改造后 |
| --- | --- | --- |
| 播放时后台负载 | 无预生成，但拖动时队列入队 | 同（无预生成）+ 拖动丢弃旧队列 |
| 拖动到新位置出图 | 排队 + 250 ms 冷却，最坏几秒 | 关键帧 seek + 丢弃旧任务，**首次出图更快** |
| 出图精度 | exact（准） | 最多早 1 个 GOP（可接受，性能档） |
| 二次打开同一文件 | 有磁盘缓存（今天仍写） | 保持（D2） |

---

## 2. B：关键帧 seek 用户开关

### 2.1 设置行定义

| 属性 | 值 |
| --- | --- |
| id | `video.thumbnail-keyframe-seek` |
| 位置 | 设置 → 视频 → 缩略图，放在"缩略图间隔"之后、"磁盘缓存"之前 |
| 类型 | `BoolRow` |
| 默认 | **关**（`false`） |
| 中文标题 | 关键帧快速定位 |
| 中文描述 | "打开后整片缩略图改用关键帧定位：生成更快，但个别帧可能比时间点早一个关键帧（通常 1–5 秒）。关闭时逐帧精确定位，最准也最慢。仅影响预生成，悬停预览始终精确定位。" |
| 英文 | "Keyframe seeking" / "Generate faster by seeking to keyframes; a frame may be up to one keyframe interval early. Hover previews always stay exact." |
| 写入路径 | `SettingsCatalog.cs`（新行，插在 `video.thumbnail-bucket` 之后，`:282` 之后）→ `AppSettings.ThumbnailKeyframeSeek`（默认 false，`AppSettings.cs:62` 附近）→ `MainWindow.Settings.cs` 新增 case → `ThumbnailService.SetKeyframeSeek(bool)` |
| 生效时机 | 立即（下一次提取生效，无需重启、无需重建服务） |

### 2.2 作用域（必须写清楚）

```
关键帧 seek 生效条件：job.Background == true   （预生成遍历）
                     或 slowMode == true        （慢速路径，见 §1.4）
悬停（即时模式、用户直接悬停）        → 永远 exact
```

理由：悬停是"用户正盯着看的那一帧"，精度优先；预生成是"后台填缓存"，快优先。

### 2.3 精度代价

- 5 s 桶 + 1 s GOP → 画面最多早 1 s（几乎无感）。
- 5 s 桶 + 5 s GOP（动漫常见）→ 画面最多早 5 s（≈ 一个桶，明显）。
- 缓解：若用户把"关键帧快速定位"打开又觉得偏，可以提示"把缩略图间隔调小到 2–3 秒"。
- 不建议自动降级（如"偏差 > 桶/2 就重 seek exact"）：那是两步 seek，收益立刻减半，复杂度翻倍。

### 2.4 缓存影响

- 磁盘键不含 seek 模式：`SHA-256("wiina-thumbs" + FormatVersion + 路径 + 大小 + mtime + 时长 + 桶 + 高度)`（`ThumbnailDiskCache.cs:110-126`，版本 `:39`）。
- 因此开关切换后，**旧缓存（exact 帧）与新缓存（keyframe 帧）会混用**。
- 三个选择：

| 选择 | 做法 | 代价 |
| --- | --- | --- |
| **保持 FormatVersion=2**（推荐 v1.3.8） | 接受新旧混用 | 画质不统一，但用户不会看到"错误内容" |
| 升到 3 | 一次性失效全部缓存 | 用户首次重开要重新生成 14–45 s |
| 把 seek 模式编进 key | 精确控制 | 缓存体积翻倍，跨模式不复用，不推荐 |

> 建议：v1.3.8 保持 2；如果上线后收到"缩略图对不上时间"的反馈，再升版本并写 release note。

### 2.5 收益

| 文件 | 现状（285 桶） | 打开关键帧后（估） |
| --- | --- | --- |
| 1080p HEVC hwdec | 14.1–14.9 s | **9–11 s** |
| 1080p HEVC 软解 | ~30 s | ~20–22 s |
| 4K | 更明显 | 更明显 |

---

## 3. C：即时模式 UX（无转圈、保留上一帧）

### 3.1 现状：每一次指针移动都先清空 + 转圈

```
指针移动 → ShowTimePreview()            MainWindow.xaml.cs:1262-1305
             └─ ThumbnailPeek.ShowLoading()   ← 无条件清空 PreviewImage.Source + 开转圈
                  └─ RequestThumbnailAsync()   MainWindow.Osd.cs:499
                       └─ GetAsync 返回 null → ThumbnailPeek.Hide()   :535-539
                            └─ 浮层整个消失（连上一帧都没了）
```

- `ShowLoading` 里 `PreviewImage.Source = null; LoadingRing.IsActive = true`（`ThumbnailPeek.xaml.cs:20-22`）→ **每次移动都先黑一下再转圈**。
- `Hide()` 同样清 `Source`（`:42`）→ 任何一次未命中都会闪黑。
- 因此即使内存命中（4 ms 热悬停），也会经过一次"清空 → 再赋值"的视觉跳变。

### 3.2 目标状态机

| 状态 | 浮层可见 | 图片 | 转圈 | 进入条件 |
| --- | --- | --- | --- | --- |
| `Hidden` | 否 | 清空 | 否 | 指针离开进度条 / 播放器关闭 |
| `Empty` | 是 | 黑底（`Source=null`，Border 背景就是 `#FF000000`） | **否** | 当前文件还没有任何帧（首次悬停） |
| `Showing` | 是 | **保持上一帧** | **否** | 已有帧，新位置正在取 |
| `Updated` | 是 | 新帧 | 否 | 取到帧 |
| `Unavailable` | 是 | 黑底（时间文本照常显示） | 否 | 策略禁止（预览关闭 / 生成未完成且开关关） |

关键点：**`Empty`/`Showing` 之间的区别就是"有没有上一帧"**；两者都不显示转圈。

### 3.3 实现点

| 文件 | 改动 | 说明 |
| --- | --- | --- |
| `ThumbnailPeek.xaml.cs:17-26` | `ShowLoading` → `ShowPending`：不再置空 `Source`、不再开 `LoadingRing` | 保留 `ApplySize` + 时间文本 + `Visibility=Visible` |
| `ThumbnailPeek.xaml(.cs):39-45` | `Hide` 保持清空 `Source`（下次进入是 `Empty` 而不是显示上个文件的帧） | 必须清，否则换片后残留旧帧 |
| `ThumbnailPeek.xaml` | `ProgressRing` 可以保留但不启用，或直接删除（建议删除，避免以后又有人打开它） | 少一个控件也少一条测试路径 |
| `MainWindow.xaml.cs:1262-1305` | 调用点改名，并记录"当前文件是否已有帧" | 由 `ThumbnailPeek` 自身用 `PreviewImage.Source is not null` 判断即可，无需额外字段 |
| `MainWindow.Osd.cs:535-539` | `frame is null` 时**不再 Hide**；仅当策略禁止（见 §3.4）才 Hide | 这是"保留上一帧"的核心 |
| `MainWindow.Osd.cs:541-547` | 复用同一个 `WriteableBitmap`（尺寸不变时），避免每次悬停分配 + 重新绑定 | 减少 LOH 分配与一次 `Invalidate` 抖动 |

### 3.4 必须先区分 `null` 的两种含义

`GetAsync` 现在用 `null` 同时表达：

1. **策略禁止**（预览关闭 / 生成未完成且 `preview-while-generating` 关，`ThumbnailService.cs:484-499`）→ 应该 **Hide**（没有预览是正确行为）。
2. **本次没取到**（还在排/失败）→ 应该 **保留上一帧**。

两种混在一起，"保留上一帧"就会在"策略禁止"时也显示旧帧（错误）。解决方案（推荐）：

```csharp
public enum ThumbnailOutcome { Frame, Pending, NotAllowed }
```

`GetAsync` 返回 `(ThumbnailOutcome, ThumbnailFrame?)` 或新增 `ThumbnailResult` 结构；`MainWindow` 按结果分派。改动集中在 `ThumbnailService.cs:457-577` 与 `MainWindow.Osd.cs:499-554`，单元测试可直接断言三种结果。

> 低成本替代：`MainWindow` 在调用前自己判一次 `ThumbnailPolicy.AllowsHoverPreview(...)`（需要 Service 暴露 `Mode`/`IsPendingFor`）。能用，但多一处状态重复，不如枚举干净。

### 3.5 清理点（必须保持"隐藏即清空"）

| 事件 | 现有位置 | 行为 |
| --- | --- | --- |
| 指针离开进度条 | `MainWindow.Osd.cs:759` | `Hide()` → 清图 |
| 关闭时间预览 | `MainWindow.Osd.cs:562` / `MainWindow.xaml.cs:1390` | 同上 |
| 切换模式/全屏 | `MainWindow.Modes.cs:110,137` | 同上 |
| 换片 | `SetSource`/`Clear` 在服务内（`ThumbnailService.cs:120-150`） | 浮层由上面几条路径收起 |

### 3.6 验收标准

- 在时间轴上快速左右扫动：**不出现转圈**、**不出现黑闪**（除非该文件从未取到过任何帧）。
- 从文件 A 换到文件 B 后第一次悬停：显示黑底（不显示 A 的帧）。
- 打开"生成完成前允许预览 = 关"且预生成未完成：悬停时**完全不显示浮层**（不是显示旧帧）。

---

## 4. D：为什么 macOS M1 Pro 15 s、Windows 30–50 s

### 4.1 先钉住前提

本报告无法访问那台 Mac 的实测环境，因此把"15 s / 30–50 s"当作**给定数字**，只用代码与已有数据做归因。两个关键未知：① macOS 那边生成了多少张；② Windows 这次是哪个文件/哪条解码路径（硬解还是回落软解）。§4.5 给出 10 分钟可完成的验证方法。

### 4.2 四个可量化项

| 项 | macOS / IINA | Windows / Wiina | 影响 |
| --- | --- | --- | --- |
| **张数** | **100 张固定**（IINA `FFmpegController.m:28` `THUMB_COUNT_DEFAULT 100`，间隔 = 时长/100） | **285 桶**（23:40 文件 ÷ 5 s）+1 | **2.85×** ← 最大单项 |
| **每帧取像素的方式** | FFmpeg 直接解码到 CPU（`avcodec_open2` + `sws_scale`），**没有 GPU→CPU 回读** | `hwdec` 解码到 GPU → `hwdownload` → VO 转 RGB → `screenshot-raw` 拷贝 **8.3 MB**（`MpvScreenshot.cs:118-155`） | 每帧 8–12 ms |
| **seek 策略** | `av_seek_frame(..., AVSEEK_FLAG_BACKWARD)`（只到关键帧） | `seek absolute exact`（关键帧后再逐帧解码到目标，`ThumbnailService.cs:1116-1131`） | 每帧 5–30 ms |
| **等待地板** | 同步调用，无轮询 | `Task.Delay(20)` 轮询，**每桶至少 20 ms**（`:1122-1124`） | 285 × 20 ms ≈ **5.7 s** |
| **输出尺寸** | 宽 240 px（偏好 120 × 2），JPEG 0.75 | 高 90 px（≈160×90），JPEG | IINA **编码更重**，反向抵消（说明"编码"不是原因） |
| **并发度** | 1（`NSOperationQueue.maxConcurrentOperationCount = 1`，`FFmpegController.m:74-75`） | 1（单 worker，`ThumbnailService.cs:832-836`） | **无差异**，可排除 |
| **写盘** | 全部生成完一次性写一个缓存文件 | 每桶一次原子写 + 索引更新（`:1253-1280`） | 每桶 1–3 ms |

### 4.3 预算模型（把差距算出来）

用已实测的两个数：Wiina 硬解 **29.0 ms wall/帧**、软解 **104.4 ms/帧**（`docs/performance.md:153-154`），加上 20 ms 轮询地板与后处理 ≈ 5 ms：

| 组合 | 单帧预算 | × 张数 | 总时长 |
| --- | --- | --- | --- |
| IINA / M1 Pro（估：CPU 解码 + 只到关键帧） | ~150 ms | 100 | **15.0 s** ← 与实测吻合 |
| Wiina 维护者机（硬解 + exact + 轮询） | ~50 ms | 285 | **14.1–14.9 s** ← 与实测吻合 |
| Wiina 若走**软解**（`hwdec` 回退 / 驱动不支持） | ~105–160 ms | 285 | **30–45 s** ← **落在 30–50 s 区间** |

**结论（三个层次）**：

1. **每帧效率 Wiina 更好**：50 ms vs 150 ms，Wiina 快 3 倍。慢的不是"Windows"。
2. **总量差 2.85 倍**：因为 Wiina 按 5 s 桶全片覆盖（285 张），IINA 固定 100 张。两者总时长因此接近（14.5 s vs 15 s）——**在同一台机器上，两者其实打平**。
3. **本机 30–50 s 的那 2–3 倍**，最可能是**硬解没生效**（软解 104 ms × 285 ≈ 30 s，软件路径再叠加 4K/10-bit 就是 45 s 以上），其次是 20 ms 轮询地板（固定 +5.7 s，占硬解路径的 40%）。

### 4.4 物理差距（无法用代码消除的部分）

- **统一内存 vs PCIe**：M1 Pro 的 VideoToolbox 帧与 CPU 共享内存，`sws_scale` 不经过总线拷贝；Windows 的 d3d11va 帧必须 `hwdownload` 到系统内存（8.3 MB/帧，10-bit 更高）。
- **媒体引擎**：M1 Pro 的 HEVC 解码器每帧固定开销极低；Windows 侧即使硬解，也要经过 D3D11 的设备/上下文同步。
- 这两项是"每帧固定税"，只能通过**减少张数**或**避免回读**（=用户已否决的 A1）来摊薄；在当前架构下，最有效的杠杆就是 **B（关键帧）+ 数量（桶大小）**。

### 4.5 验证方法（10 分钟，不用改代码）

1. 打开一个 1080p HEVC 文件，等预生成跑完，在 `%TEMP%\wiina-trace.log` 里找：
   - `thumbnail pre-generation: N extracted, M cached, K buckets in Ts`
   - 用 `K` 确认桶数（285 就是"张数差 2.85×"成立）。
2. 看同一份日志里有没有 `mpv: vd: Using hardware decoding (d3d11va)`；若出现 `avcodec` 软解或 `hwdec` 回退，说明走的是 104 ms 路径。
3. 对比 `thumbnail hover {t}s resolved in N ms` 的分布（`MainWindow.Osd.cs:524-526`），确认悬停是否也在同一档。
4. 若要直接对比 macOS：在 IINA 的缓存目录数缩略图条数（默认 100），确认其 `thumbnailWidth` 偏好（默认 120 → 输出 240 px）。

---

## 5. E：实施顺序 / 收益 / 风险 / 测试

| 顺序 | 改动 | 预计收益 | 风险 | 测试方案 |
| --- | --- | --- | --- | --- |
| **1** | C：即时模式 UX（无转圈 + 保留上一帧 + `ThumbnailOutcome`） | 用户直接可感（无闪黑）；不涉及性能 | 低：状态机写错会"显示上个文件的帧" | 单元测试三种 outcome；手工扫动 1080p/4K；换片用例 |
| **2** | B：关键帧开关（默认关）+ 服务侧 `keyframes` 参数 | 打开后预生成 −30~50% | 中低：精度最多早 1 GOP；缓存混用 | 单元测试 seek 参数；同一文件开/关对比总时长；抽 5 个时间点核对画面 |
| **3** | A：慢速模式重构（强制即时 + 锁 UI + 关键帧 + 最低优先级） | 低配设备"可用的即时预览" | 中：设置联动、磁盘缓存策略（D2）、持久化模式不被改写 | 设置页截图对比（锁定态）；开关慢速后拖动；重启后模式保持 |
| **4** | 可选：seek 轮询 20→5 ms | 预生成固定 −5.7 s（硬解路径约 −38%） | 无（精度不变） | 预生成总时长对比 |
| 5 | 文档更新：`docs/reference/thumbnails.md`、`docs/release/known-issues.md` | — | — | 与实现同步 |

**不做**：预缩放（A1/A2/A3）、多 worker、`vf=thumbnail`、自动两步 seek、进程级 CPU 亲和性、把桶默认值改成 10 s。

---

## 6. 证据索引（本树核对）

| 主题 | 位置 |
| --- | --- |
| 慢速模式不预生成 | `src/Wiina.App/Services/ThumbnailService.cs:655-661` |
| 慢速模式：优先级 / 冷却 / 交互避让 | `ThumbnailService.cs:814-824`、`:894-899`、`:869-873` |
| 即时模式判定与悬停延迟 | `ThumbnailService.cs:360-393`（Mode）、`:513-540`（hover delay + 取消） |
| 暂停/丢弃队列 | `ThumbnailService.cs:539`、`:642`、`CancelPendingHoverJobsLocked` |
| 任务入队与去重 | `ThumbnailService.cs:579-617` |
| headless 启动参数 | `ThumbnailService.cs:1070`（`Start(1280,720,headless:true,…)`）；`src/Wiina.Mpv/MpvPlayer.cs:156-194` |
| `hr-seek` / headless 选项 | `src/Wiina.Mpv/MpvPlayer.cs:185-190`（`hr-seek=yes` 在 `:188`） |
| seek + 轮询 | `ThumbnailService.cs:1113-1132` |
| 截图与回退 | `ThumbnailService.cs:1134-1158`、`:1160-1212` |
| C# 缩放 | `ThumbnailService.cs:1406-1485` |
| JPEG / 磁盘写 / 源键 | `ThumbnailService.cs:1253-1330`；`src/Wiina.Core/Caching/ThumbnailDiskCache.cs:39`、`:110-126` |
| 策略门槛 | `src/Wiina.Core/State/ThumbnailPolicy.cs:16`、`:29-37`、`:44-45`、`:70-77` |
| 设置行（缩略图组） | `src/Wiina.Core/Settings/SettingsCatalog.cs:242-322` |
| 设置行可用性机制 | `src/Wiina.Core/Settings/SettingsModels.cs:95-104`；`src/Wiina.App/Views/SettingsWindow.xaml.cs:485-496` |
| 缩略图设置即时应用 | `src/Wiina.App/MainWindow.Settings.cs:146-222` |
| 悬停浮层控件 | `src/Wiina.App/Controls/ThumbnailPeek.xaml.cs:17-45`；`ThumbnailPeek.xaml`（`Border Background="#FF000000"`、`ProgressRing`） |
| 悬停入口 / 请求 / 清空 | `src/Wiina.App/MainWindow.xaml.cs:1262-1305`；`src/Wiina.App/MainWindow.Osd.cs:499-562` |
| 实测数字 | `docs/performance.md:153-154`、`:182-184` |
| IINA 对照 | `docs/reference/iina-thumbnails.md`（100 张、`duration/100`、`AVSEEK_FLAG_BACKWARD`、JPEG 0.75、`thumbnailWidth` 120×2） |

### 已知限制

- 本轮未做任何实测（按要求只出方案）；§4 的预算模型基于 `docs/performance.md` 的既有数字与静态代码，属于**推理**而非测量。
- "30–50 s 是软解"是最强假设，需要 §4.5 第 2 步的日志确认；若确认是硬解，则主因回落到"张数 + 轮询地板"。
- 行号以本轮核对时的树为准；该树有他人未提交的修改（`git status` 可见），实现前请重新 grep 锚点代码，不要只信行号。
## 7. 实施状态（1.3.9）

### 已实施

| 编号 | 方案 | 落地 | 关键代码 |
| --- | --- | --- | --- |
| A | 慢速模式 = 即时预览 + 保留磁盘 + 设置界面锁死冲突项 | ✅ | `ThumbnailPolicy.EffectiveMode`（`src/Wiina.Core/State/ThumbnailPolicy.cs:56-58`）、`AllowsDiskCache(mode, slowMode)`（`:47-48`）、`RequiresHoverDelay(mode, slowMode)`（`:71`） |
| B | 关键帧 seek 开关（默认关，预生成/慢速有效，悬停精确） | ✅ | `video.thumbnail-keyframe-seek`（`SettingsCatalog.cs:308-314`）、`KeyframeSeek`（`ThumbnailService.cs:452-468`）、`seek absolute+keyframes`（`:1193-1240`） |
| C | 即时模式 UX：无转圈、保留上一帧、空卡 | ✅ | `ThumbnailHoverState`（`src/Wiina.Core/State/ThumbnailHoverState.cs`）、`ThumbnailOutcome` / `ThumbnailResult`（`ThumbnailService.cs:22-32`）、`ThumbnailPeek.ShowEmpty` |

### 与方案的差异

- **不做 20 ms → 5 ms 轮询**：方案把它列为零风险提速项，但本轮没有改 `SeekAsync` 的 20 ms 间隔（`ThumbnailService.cs:1205`）；关键帧定位是另一条提速路径。若后续要继续压缩预生成时间，这是首选。
- **不做 `vf=scale` 预缩放**：方案 §0.2 已判定收益抵不上硬解损失，保持不做。
- **慢速模式仍写磁盘**：方案要求慢速保留磁盘策略，已按此实现（`AllowsDiskCache` 对慢速返回 true）。
- **设置行锁死方式**：用通用的 `AvailableWhen` / `ConditionalUnavailableReason`（`SettingsModels.cs:103-127`）在刷新时置灰，而不是为每个模式写专用分支。
- **验证数据**：关键帧预生成 208 ms/帧、慢速磁盘命中 26–29 ms、Pending ≈124 ms、Frame 244–483 ms 来自 `artifacts/v139-thumb-traces.txt`；未重新测预生成总时长。
- **仍是分析的部分**：D 的 macOS/Windows 预算模型没有复测；E 的“慢速模式暂停时中心向外生成”没有实现，慢速=即时预览是当前取舍。

