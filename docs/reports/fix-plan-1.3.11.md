# Wiina 1.3.11 修复清单（待确认）

> 状态：**只读定位完成，尚未修改任何代码**。
> 基线：`feat/v1.3.10` @ `11df58c`（1.3.10 PR #19 仍打开、未合并 `main`）。
> 方法：源码走查 + 1.3.10 产物 UI Automation 实测（mini 播放器）+ 临时帧抓取实验；未改源码/设置。
> 关联报告：[WinUI 3 覆盖检查](winui3-audit-1.3.11.md) · [文档归档审计](docs-audit-1.3.11.md)。

---

## 0. 总表

| ID | 问题 | 严重度 | 根因 | 置信度 | 主要改动面 |
|---|---|---|---|---|---|
| P0-1 | 播放列表多选上移/下移只移动最上方一项 | P0 | `MoveFirstSelected` 只取 `FirstOrDefault`，`MoveRequested` 只能表达单条 | **已确认** | `PlaylistView.*`、`MainWindow.Sidebar.cs`、`Core/PlaylistMove.cs` |
| P0-2 | 毛玻璃下侧栏展开“一半闪白/黑、一半有毛玻璃” | P0 | 1.3.10 的 `Reapply` 在滑入动画**开始时**执行，`Apply` 用 `TransformToVisual(root)` 把 `TranslateX` 烘进变换，320px 源图被横移后留下半幅空隙 | **高（有回归链）** | `ChromeBlur.cs`、`SidebarHost.xaml.cs` |
| P0-3 | 预生成+“生成完成后才显示”期间仍出现黑色预览 | P0 | 悬停开始无条件 `ShowLoading`；`NotAllowed` 又走 `ShowEmpty`，两者都画黑底卡片 | **已确认** | `MainWindow.xaml.cs`、`MainWindow.Osd.cs`、`ThumbnailPeek.*` |
| P0-4 | mini 窗口会跟着鼠标走（没按键）、进度/音量条拖不动 | P0 | `MiniPlayerView.Root` 的 `PointerPressed/Moved` 不判断左键、不排除 Slider/Button；子控件处理了 Released 时 `_dragCandidate` 不会清 | **已确认（UIA 实测 + 代码）** | `MiniPlayerView.xaml.cs`、`MainWindow.Playback.cs`（主窗拖拽同理） |
| P0-5 | mini 播放列表应从下方展开（现在盖在右侧） | P0 | `PlaylistPanel` 是 `Grid.RowSpan=2` 右侧覆盖层，窗口却按 +300 高度增高 | **已确认（截图 370×581）** | `MiniPlayerView.xaml`、`MiniPlayerLayout.cs`、`MainWindow.Modes.cs` |
| P0-6 | mini 允许全屏 | P0 | `RequestMode(Fullscreen)` 没有 Mini 守卫；键盘/菜单仍可触发 | **已确认** | `MainWindow.Modes.cs`、快捷键/菜单路径 |
| P1-1 | 拖到屏幕边缘时窗口发抖 | P1 | 未定论：疑似 `ApplySnapFrame` 在 16ms 拖拽泵里反复改 DWM 风格，或 `SnapMovingRect` 锚点/工作区在多显示器边界抖动 | **中（需实机埋点）** | `MainWindow.xaml.cs`、`WindowInterop.cs` |
| P1-2 | 缩放时右侧最小化/最大化/关闭慢一拍，视频也慢一拍 | P1 | 视频等 80ms `_resizeTimer` 防抖；右侧按钮是自绘 XAML，跟非客户区不同帧 | **高** | `MainWindow.xaml.cs`（视频即时布局）；caption 按钮**本版不改原生**，只做同步缓解并记入后续 UI 修复 |
| P1-3 | mini 被藏在“高级 → 模式”，且纯音频不自动进入 | P1 | 只有 `advanced.mini-player` 一个入口；`AutoSwitchToMusicMode` 是死设置 | **已确认** | `MenuCatalog`、`OscToolbar`、`SettingsCatalog`、`MainWindow.Modes/Playback` |
| P1-4 | mini 上一首/下一首不遵循“快退/快进 5 秒”设置 | P1 | mini 直接调 `PlaylistPrevious/Next`，绕过 `OscPrevious/Next` | **已确认** | `MainWindow.Modes.cs` |
| P1-5 | mini 其他体验问题（音量气泡位置、进度条圆球被截断、首字母是 `[`、统计面板残留） | P1 | 固定 Margin 的右侧气泡；24px 行装 32px Slider；直接取文件名首字符；进入 mini 未隐藏 StatsPanel | **已确认（UIA 实测）** | `MiniPlayerView.*`、`MiniPlayerLayout.cs`、`MainWindow.Modes.cs` |
| D-1 | 过时文档需要归档 | P1 | 历史计划/阶段验收/一次性分析报告混在 `docs/` 主树 | 已审计 | `docs/**`、链接 |
| R-1 | 原生 WinUI 3 覆盖检查报告 | 交付物 | — | 已完成初稿 | `docs/reports/winui3-audit-1.3.11.md` |

---

## 1. P0-1 播放列表多选移动

### 现状

- `Controls/PlaylistView.xaml.cs:227-243` `MoveFirstSelected(delta)` 只取 `SelectedRows().FirstOrDefault()`，因此多选时只移动最上面一项。
- `MoveRequested` 事件只有一个 `(From, To)`，`MainWindow.Sidebar.cs:496` 一次只发一条 `playlist-move`。
- 单选下移已在 1.3.10 修好（`PlaylistMove.ResolveTarget`），多选仍是旧行为。

### 期望

- 多选上移：所有选中项各上移一格，保持相对顺序；首项已在 0 时不动，其余尽量上移（或整体不动，需确认）。
- 多选下移：所有选中项各下移一格，保持相对顺序；末项已在末尾时不动。
- 连续选择时整体作为一块移动（例如 `[A,B,C,D]` 选 `B,C` 下移 → `[A,D,B,C]`）。
- 移动后所有原选中项仍选中（1.3.10 已按 Path 恢复）。

### 修复方案

1. `Core/PlaylistMove.cs` 新增纯函数：
   ```csharp
   public static IReadOnlyList<(int From, int To)> ResolveBatch(
       IReadOnlyList<int> selectedIndexes, int count, int delta)
   ```
   - 去重、排序；
   - 上移按**升序**发送（先移最上面，避免索引下移）；
   - 下移按**降序**发送（先移最下面，避免索引上移）；
   - 每项用 `ResolveTarget(from, from+delta)`（下移仍传 `to+1` 的 mpv 语义）；
   - 越界/重复/同一项的项跳过。
2. `PlaylistView` 把 `MoveRequested` 改为批量事件（保留旧单条事件或新增 `MoveBatchRequested`），`MainWindow.Sidebar` 顺序发送多条 `playlist-move`（mpv 命令队列保序）。
3. 按钮/右键菜单启用态按“所有选中项”判断：上移时若最小索引为 0 且全选连续到顶则禁用；下移同理。
4. 测试：连续、非连续、含首/尾、全选、单选、空选；`ResolveBatch` 发出顺序用模拟列表验证最终顺序。

### 验收

- 3 项选 2 项上/下移；5 项非连续选 3 项；全选；移动后选中保持；拖拽仍正常。

---

## 2. P0-2 侧栏毛玻璃“一半闪白/黑、一半有毛玻璃”

### 根因链（1.3.10 回归）

1. 1.3.10 在 `SidebarHost.PlayShow` 里 `Visibility = Visible` 后立刻调用 `RequestBlurReapply()`（`SidebarHost.xaml.cs` 的 `PlayShow`/`RequestBlurReapply`）。
2. 此时 `SlideTransform.TranslateX` 仍是 `±Width`（或动画中），侧栏还没滑到位。
3. `ChromeBlurLayer.Apply` 用 `Bounds(this, root)` → `TransformToVisual(root)` 计算 `surface`，**把侧栏的滑入位移烘进了 `_transform.TranslateX`**。
4. 模糊源图只有 320px 宽，映射到视频宽度后横移一个侧栏宽度，侧栏内就会有一部分没有图 → 露出 `ChromeSolidBrush`（浅色 `#F3F3F3` / 深色 `#202227`），另一部分有图 → “一半闪白/黑、一半毛玻璃”。
5. 下一次抓帧（≤250ms）或动画结束后才会纠正；Fixed 档更久。

### 修复方案

1. **动画期间不要 `Apply`/`Reapply`**：`SidebarHost` 增加 `_animating` 标志（`PlayShow`/`PlayHide` 置位，`OnAnimationTick` 结束时清除）；`ChromeBlurLayer` 增加 `IsTransformSuppressed` 或由 `SidebarHost` 在动画期间跳过该层。
2. 动画结束后（`TranslateX == 0`）再 `RequestBlurReapply()`；`PlayShow` 的立即 reapply 移到动画完成回调。
3. 更根本：`ChromeBlurLayer.Apply` 计算 `surface` 时排除祖先的 `RenderTransform`（用层父容器坐标系，或减去 `SlideTransform.TranslateX`），这样即使动画中抓帧也不会错位。
4. `ApplyBlurVisuals` 在动画期间不要显示 tint，避免空层+ tint 叠加。
5. 验证：在 20ms 帧抓取（`CopyFromScreen` + 窗口置顶）下逐帧检查侧栏矩形内不出现整块 `#F3F3F3`/`#202227`；浅/深 × 固定/动态/始终；首次打开与再次打开。

### 备注

- 这个问题是 1.3.10 “首帧重放”修复引入的回归；用户说“修不好也可以先全闪，现在一半闪更难受”，所以本版宁可动画期间保持**均匀纯色**，也不要半幅错位。

---

## 3. P0-3 预生成期间黑色缩略图预览

### 现状

- 悬停开始：`MainWindow.xaml.cs:1312` 无条件 `ThumbnailPeek.ShowLoading(...)`，`ThumbnailPeek.xaml:15` 的 `Root.Background="#FF000000"` + `ProgressRing` → 黑底卡片。
- 结果返回：`MainWindow.Osd.cs:543-556` 对 `Unavailable` / `Empty` 调 `ShowEmpty(...)`，仍是黑底卡片。
- 用户的设置：预生成 + “生成完成后才显示”（`ThumbnailPreviewWhileGenerating=false`）。生成期间 `GetAsync` 返回 `NotAllowed`，于是黑卡出现。

### 期望

- 生成完成前：**什么都不显示**（只有时间文本/时间气泡可以显示），不要黑底卡片、不要转圈。
- 生成完成后：正常显示缩略图。
- 如果用户打开“生成完成前允许预览”，则保持现有 `Pending` 行为（保留上一帧，不转圈）。

### 修复方案

1. `ThumbnailPeek` 增加 `ShowUnavailable()`/`HidePreviewOnly()`：只保留时间文本，或直接 `Hide()`；`ShowLoading` 在 `PreviewImage.Source == null` 时不要显示黑底（至少把 `Root.Background` 改为透明/主题面板色，或整卡隐藏）。
2. `ShowTimePreview` 先问服务/策略 `CanShowHoverPreviewNow(...)`（预生成是否完成、是否允许生成中预览）；不允许时只显示时间气泡，不调用 `ShowLoading`。
3. `RequestThumbnailAsync` 的 `Unavailable`/`Empty` 分支在无历史帧时 `Hide()`，不要 `ShowEmpty`；有历史帧时按现有 `Pending` 保留。
4. 测试：`ThumbnailPolicy` 增加“生成中不显示”用例；手动 4K 预生成观察前 30 秒无黑卡。

---

## 4. P0-4 mini 窗口跟随鼠标 / 进度条与音量条拖不动

### 实测（1.3.10 UI Automation）

- mini 窗口 400×297；`ProgressSlider` 的 UIA 矩形 `288×24`（行高 24，WinUI Slider 默认 32 → 圆球上下被裁）。
- 音量气泡 `VolumeSlider` 在 `(309,296) 122×32`，而音量按钮在 `(131,456) 30×30`：气泡在封面右上方，离按钮约 160px。
- 点击音量按钮后 1s 自动收起；拖拽时窗口会跟着走（用户报告）。

### 根因

- `MiniPlayerView.Root` 的 `PointerPressed` 对任何子控件按下都置 `_dragCandidate=true`；`OnPointerMoved` 不检查左键，移动超过 3px 就 `CapturePointer` 并 `DragMoved` → 拖 Slider/按钮变成拖窗口。
- `OnPointerReleased` 只在 Root 收到释放时清 `_dragCandidate`；Slider/Button 处理了 Released 时 Root 收不到，标志残留；下一次鼠标移动（无按键）仍会拖窗口。
- `MainWindow.Playback.cs` 的内容区拖拽 `DragWindowFromCursor` 也只看 `_windowDragCandidate`，直接 `PointerMoved` 路径没有左键检查；释放丢失后同样会跟随鼠标。

### 修复方案

1. mini：`OnPointerPressed` 只对“非交互控件”启动拖拽候选（`e.OriginalSource` 向上找 `ButtonBase`/`RangeBase`/`ListViewItem`/`TextBox` 等则直接 return）；`OnPointerMoved` 先检查 `WindowInterop.IsKeyDown(VirtualKey.LeftButton)`，没按住就清 `_dragCandidate/_dragging` 并 return。
2. 主窗：`DragWindowFromCursor` 开头检查左键；未按住时清 `_windowDragCandidate/_windowDragging` 并停泵。
3. `OnPointerReleased`/`PointerCaptureLost`/`PointerExited` 统一清态。
4. 进度条：行高 24 → 32（控制区 72 → 80），或做 24px 紧凑 Slider 样式（thumb 12、track 3），确保圆球完整可拖；`ProgressSlider` 的 `PointerPressed/Released` 与 `SetPosition` 的 `_seeking` 逻辑保留。
5. 音量气泡：改为 `Popup`/`Canvas` 定位，锚定 `MuteButton` 上方 8px，限制在窗口内；点击音量按钮只负责开气泡，气泡内按钮静音；滚轮调音量时同样定位。
6. 验证：拖动进度/音量时窗口坐标不变；松手后移动鼠标不拖窗；圆球完整；气泡紧贴音量按钮。

---

## 5. P0-5 mini 播放列表改为下方展开

### 现状

- `MiniPlayerView.xaml:185-214`：`PlaylistPanel` `Grid.RowSpan="2"`、`Width=300`、`HorizontalAlignment=Right`、`Margin="0,0,0,72"` → 右侧覆盖封面。
- `MainWindow.Modes.ResizeMiniForPlaylist` 在展开时把窗口高度 +300，但多出来的高度被右侧面板覆盖，左侧封面留空。
- 实测：400×297 → 384×588，播放列表占右侧 300 宽、封面左侧一条。

### 期望（spec §10.1/§10.2）

- 高度 = 72 + 封面 + 展开的 300；播放列表在**下方**占一整块 300 高区域，宽度随窗口。
- 收起时窗口恢复；列表自动收起阈值 200 保留。

### 修复方案

1. `MiniPlayerView.xaml` 根 Grid 加第三行 `RowDefinition Height="Auto"`；`PlaylistPanel` 改为 `Grid.Row="2"`、`Height="300"`、`HorizontalAlignment="Stretch"`、`Margin="0"`、`Visibility=Collapsed`。
2. `ResizeMiniForPlaylist` 的高度计算不变（+300），但确认 `HeightWithPlaylist` 与 XAML 行高一致；窗口增长后封面用 `*` 保持原比例。
3. 列表行宽随窗口，去掉右侧覆盖的 `BorderThickness` 方向；保留标题/关闭按钮。
4. 验证：展开时封面不变形、列表在下方、控制行仍在列表上方、收起后窗口恢复。

---

## 6. P0-6 mini 禁止全屏

### 现状

- `OnRootPreviewKeyDown` 在 mini 下仍执行 `TryExecuteShortcut`；`F`/`Enter` 映射 `ToggleFullscreen` → `RequestMode(Fullscreen)`。
- `MainWindow.Modes.ApplyMode(Fullscreen)` 会 `HideMiniPlayer()` 后进入全屏，未拒绝。
- 菜单/标题栏全屏入口在 mini 下仍可能触发。

### 修复方案

1. `RequestMode` 或 `ApplyMode`：`if (_mode.Current == PlayerMode.Mini && target == PlayerMode.Fullscreen) { ShowOsd(...); return; }`。
2. `ToggleFullscreen()`/`ExecuteAction(ToggleFullscreen)` 在 mini 下直接 return（不弹 OSD 或提示“mini 不支持全屏”）。
3. 菜单全屏项在 mini 下禁用/勾选态不出现；`TitleBar`/OSC 全屏按钮在 mini 不可达（已隐藏）。
4. 验证：mini 下按 F/Enter/双击（双击是返回主窗，不是全屏）、菜单全屏均不进入全屏。

---

## 7. P1-1 边缘吸附“窗口发抖”

### 现状与证据

- 吸附逻辑：`MainWindow.xaml.cs:2076-2148`（`SnapWindowToEdges`/`SnapMovingRect`）、`DragWindowFromCursor:2653-2718`（16ms 泵）、`ApplySnapFrame:2167`。
- DWM 风格切换：`WindowInterop.SetWindowFrame` 改圆角/边框；实测 `DWMWA_EXTENDED_FRAME_BOUNDS` 在切换前后**不变**（margins 7,0,7,7），所以不是边框尺寸变化。
- 仍有两个高概率来源：
  1. **双移动源**：视频区拖拽用 `AppWindow.Move`，若指针同时落在标题栏 caption 区域，OS 也在移动窗口；两者叠加会抖。
  2. **锚点/工作区边界**：`SnapMovingRect` 的 `_moveAnchor` 来自首条 `WM_MOVING` 的 rect；若首条 rect 已是“已吸附”位置，或 `WorkAreaUnderPointer` 在多显示器/任务栏边界切换，raw 与 work area 会来回跳，`ApplySnapFrame` 随之切换 DWM 风格。
- 另：`ApplySnapFrame` 在 16ms 泵里每次都可能改 DWM 属性（只在 `_frameStyleApplied` 变化时实际调用，但状态切换仍频繁）。

### 修复方案（先埋点后定案）

1. 在 `SnapMovingRect`/`DragWindowFromCursor`/`ApplySnapFrame` 加 `Trace`：raw、applied、edges、work area、cursor、frame style；复现时抓 10 秒日志。
2. 逻辑加固：
   - 每次手势只捕获一次锚点；**不要在 WM_MOVING 中重置**；`WM_ENTERSIZEMOVE`/视频拖拽开始时重置。
   - `ApplySnapFrame` 只在边缘集合**稳定 100–150ms** 后切换，或在 `WM_EXITSIZEMOVE`/拖拽结束时统一切换，避免拖拽中反复改 DWM。
   - `WorkAreaUnderPointer` 优先用**窗口中心/所在显示器**，边界处不随指针跳；或把工作区固定在手势开始时的显示器。
   - 视频区拖拽时禁用 native caption 拖拽（`SetCaptionDragEnabled(false)` 仅在拖拽中），避免双移动源。
3. 添加纯逻辑测试：给定连续 raw 矩形序列，输出位置必须单调（不出现 A→B→A 抖动）；边界/多显示器用例。
4. 验收：真实鼠标拖到上/下/左/右边缘，各停 3 秒，窗口位置稳定；多显示器边界；任务栏自动隐藏；100%/150% DPI。

> 需要你帮忙：如果修复后仍能复现，请给 5–10 秒录屏或告诉我显示器数量/缩放/任务栏位置，便于锁定“双移动源”还是“工作区切换”。

---

## 8. P1-2 缩放“慢一拍”（右侧按钮 + 视频）

### 根因

- `MainWindow.xaml.cs:227` `_resizeTimer` 间隔 **80ms**；`OnVideoFrameSizeChanged`（2265）和 `OnRootSizeChanged`（2271）都 `Stop/Start`，最终 `ApplyVideoLayout` 在停手 80ms 后才设置 `d3d11-composition-size` → 视频慢一拍。
- 右侧最小化/最大化/关闭是 `TitleBarView.xaml` 的**自绘 XAML Button**（`TitleBarCaptionButtonStyle`），不是系统 caption 按钮；窗口非客户区由 DWM 即时更新，XAML 内容岛晚一帧 → 按钮位置/命中慢一拍。
- `ApplyVideoLayout` 同时新建 `RectangleGeometry`、设 composition size、`CoverClientArea`，全都压在防抖后。

### 修复方案

1. 视频：`SizeChanged` 里立即（或 16ms 合并）调用轻量 `ApplyCompositionSize()`（只设 `d3d11-composition-size`/panel 尺寸），把 `Clip` 几何、`CoverClientArea` 等放回 80ms 防抖或下一帧；暂停时保留现有 zero-seek 刷新。
2. 右侧按钮（**本版按确认不改原生**）：保留 `TitleBarView` 自绘按钮，做同步缓解——在 `WM_SIZE`/`SizeChanged` 里同步更新标题栏宽度并 `UpdateLayout()`，尽量让按钮与视频边缘同帧；同时在 `known-issues.md` 与 `winui3-audit` 记录“自绘 caption 按钮比非客户区晚一帧，后续 UI 修复时改用原生 `AppWindowTitleBar` 按钮”。
3. 录 60fps 或逐帧截图验证：拖右下角缩放，视频边缘同帧；右侧按钮残余延迟如实记录，不假装已根治。

---

## 9. P1-3 mini 入口与纯音频自动进入

### 现状

- 唯一入口：设置 → 高级 → 模式 →「迷你播放器（音乐模式）」（`SettingsCatalog` `advanced.mini-player`），带“开发中”警告 + 二次确认。
- `MenuCatalog` 只有 `video.music-mode` 常量，**没有加入任何菜单组**；`OscToolbar.xaml` 只有 PiP/全屏/播放列表/更多，没有音乐模式按钮。
- `AppSettings.AutoSwitchToMusicMode`（默认 true）**没有任何读取者**；打开纯音频不会进 mini。
- spec §10/§14：Video 菜单有「音乐模式」；OSC 工具栏有 musicMode 图标；音频文件自动进入；视频文件不自动进入。

### 修复方案（已确认）

1. **入口**：
   - `MenuCatalog` 视频组加「音乐模式」勾选项（`video.music-mode`，`IsMini` 勾选态）。
   - `OscToolbar.xaml` 加音乐模式按钮（`\uE8D6` 或 `\uEA86` 合适的 Fluent 图标），`OscToolbar.MoreClicked` 同级的 `MusicModeClicked`，接到 `ToggleMini()`。
   - **移除**“高级 → 模式 → 迷你播放器（音乐模式）”及其“开发中/二次确认”；`SettingsCatalog` 删除 `advanced.mini-player`，`SettingsWindow` 对应处理删除。
   - 默认快捷键加 `Ctrl+Alt+M`（spec `⌥⌘M` 的 Windows 映射）到 `wiina-defaults.conf`；`PlayerActionCatalog` 恢复 `MusicMode`（分类“模式”），并更新 `ShortcutEditorOmitsMusicMode` 测试为“允许且绑定”。
2. **纯音频自动进入**：
   - 在 `OnTrackListChanged`/`ApplyLoadedMedia` 后判断 `MediaModeAdvice.HasRealVideo(_trackList)`；无视频轨且 `_settings.AutoSwitchToMusicMode` 且当前 Normal → `RequestMode(Mini)`。
   - 视频文件、PiP、全屏、手动退出 mini 后不反复触发（加“本文件已自动进入过”标志，用户手动退出后不再自动）。
   - **新增设置行**「音频文件自动进入音乐模式」（复用 `AppSettings.AutoSwitchToMusicMode`，默认开），替换被删除的 `advanced.mini-player` 行，保持设置行总数不减少；放在「高级 → 模式」或「界面」，英文覆盖同步。
3. 文档：`docs/reference/IINA-macOS-UI-复刻规格.md` 已写；更新 `docs/reference/settings-layout.md` 行数/本地化/行号表、`release/notes`、`known-issues`、`README` 快捷键表。
4. 验收：音频文件打开自动 mini；视频不自动；菜单/工具栏可进出；`Ctrl+Alt+M` 可用；设置关闭后不自动；删除的高级入口不再出现。

---

## 10. P1-4 mini 上一首/下一首遵循设置

### 现状

- `MainWindow.Modes.InitializeStage6`：`MiniPlayer.PreviousRequested += (_, _) => PlaylistPrevious();`、`NextRequested += PlaylistNext();`
- `OscPrevious/OscNext`（`MainWindow.Playback.cs:973-993`）已经实现了 `OscPrevNextMode.Seek`（±5s，Ctrl ±30s）与 `TrackSkip`。
- 结果：mini 的按钮永远切歌，设置只对 OSC 生效。

### 修复方案

- 把 mini 的两个事件改成 `OscPrevious`/`OscNext`（或抽 `SkipPrevious/SkipNext` 供 OSC、mini、键盘共用）。
- 如果键盘 `Ctrl+Left/Right` 也应遵循该设置，一并改（需确认；默认方案里它们是 `playlist-prev/next`）。
- 测试：设置 Seek 后 mini 上一首/下一首 seek ±5s；TrackSkip 后切歌；Ctrl 组合 ±30s（如果走 `OscPrevious`）。

---

## 11. P1-5 mini 其他体验问题

| 问题 | 根因 | 修复 |
|---|---|---|
| 音量气泡位置完全不正常 | `VolumeBubble` 固定 `Margin="0,0,12,84"`、右对齐、底部对齐；在 400 宽窗口里跑到封面右上，离音量按钮约 160px | 用 `Popup`/`Canvas` 锚定 `MuteButton` 上方 8px，限制在窗口内；滚轮/点击都定位 |
| 进度条圆球被截断 | `ProgressSlider` 在 24px 行里，WinUI Slider 默认 32px | 行高 32（控制区 80）或紧凑 Slider 样式（thumb 12/track 3/height 24） |
| 首字母是 `[` | `SetMedia` 取 `resolvedTitle[..1]`，文件名以 `[` 开头 | 跳过标点/括号，取第一个字母/数字；没有则用专辑/艺术家/♪ |
| 统计面板残留 | `EnterMiniInternal` 未隐藏 `StatsPanel`；UIA 仍能访问 | 进入 mini 隐藏 StatsPanel/OSD/ThumbnailPeek/BufferStatus/FrameDropNotice，退出恢复 |
| 点击音量按钮先开气泡、第二次才静音 | `OnMuteClick` 第一次只 `ShowVolumeBubble` | 明确交互：单击静音、滚轮/长按/音量图标打开气泡；或气泡内静音按钮即可，单击只开气泡但加“静音”独立按钮 |
| mini 下全屏 | 见 P0-6 | — |
| 播放列表行无元数据/截断 | 右侧 300 覆盖 + 行宽不足 | 下方全宽 + 标题/副标题两行 |

---

## 12. WinUI 3 覆盖检查（报告摘要）

完整报告：[winui3-audit-1.3.11.md](winui3-audit-1.3.11.md)。结论：

- **原生 WinUI 3 是骨架**：Window/AppWindow、Button/Slider/ToggleSwitch/ComboBox/NumberBox/ListView/TextBox/CheckBox/ProgressRing/ProgressBar、ContentDialog/MenuFlyout、ThemeResource、Mica/DesktopAcrylic、ThemeShadow。
- **三块必须“魔改”**：非客户区/标题栏（`WindowInterop` 子类化、DWM、caption region、content island）、视频呈现（`SwapChainPanel` + mpv composition + 1/dpi）、光标隐藏（`CursorHider` IAT hook）。
- **WinUI 风味自绘**：OSC、侧栏、时间轴、音量、mini、PiP、OSD、统计卡、设置行/搜索、图标/颜色令牌。
- **WinUI 3 尚未覆盖**：SwapChainPanel 与亚克力/飞层、非客户区 caption 按钮时序、光标、混合 DPI、PiP 的 DWM 8px 边、弹层在视频上的首帧、content island 1px 等。
- 报告里给出“原生 / 魔改 / 风味”三类清单、风险与建议（含右侧 caption 按钮改原生）。

---

## 13. 过时文档归档

完整审计：[docs-audit-1.3.11.md](docs-audit-1.3.11.md)。建议 `git mv` 到：

```
docs/archive/
  README.md
  planning/PLAN.md
  acceptance/stage-0..8.md, stage-11.md, v1-polish*.md
  reports/hang-analysis-1.2.7.md
  reports/known-issues-1.3.1.md
  reports/performance-vs-potplayer.md
  reports/ui-freeze-analysis.md
  reports/thumbnails-optimization-1.3.7.md
  reports/thumbnails-v1.3.8-plan.md
```

保留：所有 `v1.x` 验收页（发布记录）、architecture ADR、当前 reference（IINA 规格/缩略图/设置布局/快捷键）、release notes/known-issues/installer/file-associations/licensing/accessibility/performance（更新版本号即可）。

移动后需要同步链接（`CONTRIBUTING.md`、`docs/acceptance/README.md`、`docs/reference/README.md`、`docs/reference/thumbnails.md`、`docs/release/notes.md`、`docs/release/known-issues.md`、历史验收页），并跑一次链接检查；旧路径保留一页“已归档”重定向 stub，避免外部链接 404。

---

## 14. 修复分组（拟）

| 组 | 负责 | 写入范围 | 依赖 |
|---|---|---|---|
| A | P0-1 多选移动 + `ResolveBatch` + 测试 | `Controls/PlaylistView.*`、`MainWindow.Sidebar.cs`、`Core/PlaylistMove.cs`、tests | 无 |
| B | P0-2 侧栏毛玻璃动画 + P0-3 缩略图黑卡 | `ChromeBlur.cs`、`SidebarHost.xaml.cs`、`ThumbnailPeek.*`、`MainWindow.xaml.cs`（ShowTimePreview）、`MainWindow.Osd.cs` | 无 |
| C | P0-4/5/6/P1-4/P1-5 mini 交互与布局 | `MiniPlayerView.*`、`MiniPlayerLayout.cs`、`MainWindow.Modes.cs`、`MainWindow.Playback.cs`（主窗拖拽左键检查） | 无 |
| D | P1-3 mini 入口/自动进入/快捷键 | `MenuCatalog.cs`、`OscToolbar.*`、`SettingsCatalog.cs`、`SettingsLocalization.cs`、`AppSettings.cs`、`MainWindow.Modes.cs`、`MainWindow.Playback.cs`、`Core/Input/wiina-defaults.conf`、`PlayerActionCatalog`、按键测试 | **blocked_by C**（都要动 `MainWindow.Modes.cs`/`Playback.cs`） |
| E | P1-1 吸附 + P1-2 缩放（caption 保留自绘，只做同步缓解） | `MainWindow.xaml.cs`、`WindowInterop.cs`、`TitleBarView.*`、`Core/State/EdgeSnap.cs`、tests | **blocked_by B**（都要动 `MainWindow.xaml.cs`） |
| F | Lead：文档归档、WinUI 报告、版本、构建、PR | `docs/**`、版本源、脚本、README | 全部完成 |

冲突预案：B 与 E 都碰 `MainWindow.xaml.cs`，E 在 B 之后；C 与 D 都碰 `MainWindow.Modes.cs`/`Playback.cs`，D 在 C 之后；A 独立。

---

## 15. 交付流程（1.3.11）

1. 分支：`feat/v1.3.11`，**从 `feat/v1.3.10` 切出**（PR #19 仍打开；若你先合并 #19，我 rebase 到 main）。不合并 main。
2. 按 §14 分组；Lead review、构建、测试。
3. 构建：`dotnet build src/Wiina.App/Wiina.App.csproj -c Release -p:Platform=x64`；测试用 1.3.10 同款 in-process runner（vstest testhost 本机受限）。
4. `scripts/release.ps1 -Version 1.3.11` → zip + setup；记录 SHA-256/字节数。
5. 文档：`docs/acceptance/v1.3.11.md`、`release/notes.md`、`known-issues.md`、`reference/settings-layout.md`、`reference/iina-shortcuts.md`、`README.md`、本文件实施记录、WinUI 报告、归档索引。
6. PR：`feat/v1.3.11` → `main`，不合并；附测试/产物/截图/未解决风险。
7. 验收（computer use）：多选移动、侧栏逐帧、缩略图预生成、mini 拖拽/进度/音量/列表/全屏、音频自动 mini、吸附抖动、缩放按钮/视频同帧。

---

## 16. 已确认的决定（2026-11-08）

1. 「播放列表下方展开」= **mini 播放器**的播放列表；主窗口侧栏播放列表不改。
2. mini 只保留**主窗口内覆盖层**，不改成独立窗口；按 spec §10.2 把列表放到底部。
3. 右侧 caption 按钮**本版不改原生**，保留自绘；只做同步缓解，并把“后续 UI 修复改用原生 AppWindowTitleBar”写入 `known-issues.md` 与 WinUI 报告。
4. mini 入口：**移除**高级 → 模式里的开发者入口；加**视频菜单 + OSC 工具栏**，并加默认 `Ctrl+Alt+M`；`PlayerActionCatalog` 恢复 `MusicMode`，更新相关测试。
5. 纯音频自动进入 mini **默认开**；设置里加一行「音频文件自动进入音乐模式」（复用 `AutoSwitchToMusicMode`），可关闭。
6. 吸附抖动：先**加固 + 埋点**，不等录屏；修后仍复现再按 trace 定案。
7. 1.3.11 从 `feat/v1.3.10` 切（stacked PR）；**不合并**，由你人工操作。
8. 文档归档：§13 的 19 个文件全部移动；关键旧路径保留重定向 stub；链接重写 + linkcheck。

---

## 17. 定位轮的边界（已完成）

- 定位轮未修改任何源码、测试、设置、版本号。
- 定位轮未移动/删除任何文档；归档清单仅提案。
- 定位轮未提交 commit、未 push、未创建 PR。
- 动态验证仅使用 UI Automation 与临时截图，未改用户 `settings.json`。
- 进入实施轮后：分支 `feat/v1.3.11` 已创建；下方 §18 记录实施结果。
