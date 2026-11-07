# Wiina 1.3.10 修复清单（待确认）

> 状态：**只读定位完成，尚未修改任何代码**。
> 基线：`main` @ `6d2155f`（v1.3.9 之后仅文档提交）。
> 方法：源码走查 + libmpv 0.41 最小实验 + 1.3.9 打包产物真机复现（`bin/x64/Release`，ProductVersion `1.3.9+c004876`）。
> 说明：`bin/Release` 下另有一份 **1.0.2 旧产物**（10-19），不能用于回归；后续构建与验收统一使用 `bin/x64/Release` 或 `artifacts/`。

---

## 0. 总表

| # | 问题 | 严重度 | 根因摘要 | 置信度 | 主要改动面 | 风险 |
|---|---|---|---|---|---|---|
| 1 | OSC 侧栏播放列表「下移」无效 | P0 | mpv `playlist-move old new` 在向下移动时按「删除后索引」插入，应用直接传了目标索引 | **已实证** | `MainWindow.Sidebar.cs`、`Core` 新增纯逻辑 + 测试 | 低 |
| 2 | 毛玻璃模式下侧栏快速设置展开闪白/黑底 | P1 | 侧栏折叠时模糊层尺寸为 0、从未拿到帧，展开时先露出不透明 `ChromeSolidBrush` | 高（代码+现象吻合） | `ChromeBlur.cs`、`SidebarHost.xaml.cs` | 低 |
| 3 | 默认快捷键绑定系统音量键 | P0 | `wiina-defaults.conf` 末尾把 `VOLUME_UP/DOWN/MUTE` 绑到 mpv 音量；按键同时被 Windows 全局处理 | **已确认** | `wiina-defaults.conf`、`mpvnet/wiina-input.conf`、文档、测试 | 低 |
| 4 | 播放时无法阻止熄屏/睡眠/锁屏 | P1 | 全仓无 `SetThreadExecutionState` / `PowerCreateRequest` | **已确认（0 命中）** | 新增电源服务 + 设置行 + 调用点 | 低 |
| 5 | 长时间播放概率黑屏（mpv 视频输出停住） | P0 | 8 Hz 全帧回读无退避/暂停跳过；设备丢失恢复有粘滞开关；无「时间在走但没新帧」看门狗；无睡眠/显示变化处理 | 中高（机制明确，需长跑回归） | `ChromeBlur.cs`、`MainWindow.xaml.cs`、`WindowInterop.cs` | 中 |
| 6 | 检测中顺带发现的其他问题 | P1/P2 | 见 §7 | — | 见 §7 | 低 |

---

## 1. 复现环境与素材

- 构建产物：`src/Wiina.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/Wiina.exe`（1.3.9）。
- 测试媒体：
  - `C:\Users\zheng\Desktop\Folder\测试\autoplay\a.avi` / `b.avi`
  - `C:\Users\zheng\Desktop\Folder\测试\test1\...16bit...mkv`（1080p HEVC，23:40）
  - `artifacts\a.avi` / `b.avi` / `c.avi`（"BLUR TEST 47" 彩条，4 s，适合毛玻璃逐帧对比）
- 构建工具：系统 PATH 上的 `dotnet` 只有运行时；**SDK 在 `C:\Users\zheng\.dotnet\dotnet.exe`（10.0.402）**，构建/测试需显式使用该路径，或把 `C:\Users\zheng\.dotnet` 放到 PATH 最前。
- Inno Setup 6 已安装：`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`，可产出安装包。
- `gh` CLI 未安装；PR 走「push 分支 + GitHub compare 链接」，或在拿到可用 token 后用 REST 创建（见 §9）。

---

## 2. Bug 1：播放列表「下移」按钮无效（P0）

### 2.1 现象

1. 打开多个文件 → `Ctrl+Shift+B` 打开播放列表侧栏。
2. 选中第 1 项，点「上移」→ 不动（正确，已在顶部）。
3. 选中第 2 项，点「上移」→ 顺序变化（**正常**）。
4. 选中第 1 项或第 2 项，点「下移」→ **顺序完全不变**；拖拽可排序。

已在 1.3.9 产物上真机复现：`[a,b,c]` 选中 `a`（索引 0）点下移无反应；选中 `b`（索引 1）点上移后变为 `[b,a,c]`。

### 2.2 根因（已用 libmpv 实证）

调用链：

- `Controls/PlaylistView.xaml.cs:101-105` `OnMoveDownClick → MoveFirstSelected(+1)`
- `Controls/PlaylistView.xaml.cs:205-220` 计算 `to = row.Index + delta`
- `MainWindow.Sidebar.cs:494-505` `MovePlaylistItem(from, to)` → `playlist-move from to`

mpv 的 `playlist-move <old> <new>` 语义不对称：**向上移动时 `new` 是最终索引；向下移动时插入位置按“删除 old 之后”的列表计算，等价于 `new-1`**。因此：

- 上移 `to < from`：直接传 `to` 正确。
- 下移 `to > from`：必须传 `to + 1`；直接传 `to` 会退回到原位（相邻下移就是完全 no-op），且 mpv 仍返回成功，应用无从报错。

libmpv 最小实验（`vo=null`，3 项 `[a,b,c]`，逐条执行）：

| 当前列表 | 命令 | 结果 | 结论 |
|---|---|---|---|
| `[a,b,c]` | `playlist-move 0 1` | `[a,b,c]` | **相邻下移 no-op** |
| `[a,b,c]` | `playlist-move 0 2` | `[b,a,c]` | 下移一格需 +1 |
| `[b,a,c]` | `playlist-move 0 3` | `[a,c,b]` | `new == count` 合法，移到末尾 |
| `[a,c,b]` | `playlist-move 2 0` | `[b,a,c]` | 上移按最终索引 |
| `[b,a,c]` | `playlist-move 2 1` | `[b,c,a]` | 上移一格按最终索引 |

同一处也影响**向下拖拽重排**：`OnDragItemsCompleted` 传的 `to` 是最终索引，向下拖会少一格。

### 2.3 修复方案

1. 新增纯逻辑 `src/Wiina.Core/Files/PlaylistMove.cs`（或 `Input/PlaylistMove.cs`）：
   ```csharp
   public static int ResolveTarget(int from, int to) =>
       to > from ? to + 1 : to;
   ```
   并在调用侧校验 `0 <= from < count`、`0 <= to < count`、`from != to`。
2. `MainWindow.Sidebar.cs` 的 `MovePlaylistItem` 用 `PlaylistMove.ResolveTarget(from, to)` 发命令。
3. 补 `tests/Wiina.Core.Tests/PlaylistMoveTests.cs`：上移/下移/相邻下移/移到末尾（`to+1 == count`）/非法索引。
4. 顺带修 §7.3 的「移动后选中丢失」，否则连续点下移仍要重新选中。

### 2.4 验证

- 单元测试覆盖所有映射。
- 真机：`[a,b,c]` 选中 `a` 连点两次下移 → `[b,c,a]`；选中 `c` 连点两次上移 → `[c,a,b]`；拖拽向上/向下各一次，顺序与侧栏一致。
- 命令失败/越界时写 trace 并 OSD 提示（不静默）。

---

## 3. Bug 2：毛玻璃模式下侧栏「快速设置」展开闪白/黑底（P1）

> 已确认：用户指的是**侧栏快速设置**（Layout / Video / Audio / Subtitles），不是 OSC `⋯` 展开菜单。

### 3.1 现象

启用「毛玻璃模式」（固定/动态/始终任一档）后，点标题栏「快速设置」按钮（或 `Ctrl+Shift+S`）展开侧栏：

- 浅色主题：展开瞬间闪一块**近白**底（`#F3F3F3`）。
- 深色主题：闪一块**近黑**底（`#202227`）。
- 纯色档（Solid）不闪；侧栏在已显示过一次后再次展开，偶尔不闪。

### 3.2 根因

链路：

- `Controls/SidebarHost.xaml:97` `PanelBorder.Background="{ThemeResource ChromeSolidBrush}"`；`BackdropBlurHost` 里是 `ChromeBlurLayer`；`SidebarBlurTintLayer` 在模糊层之上。
- `ChromeBlurLayer.Apply`（`Interop/ChromeBlur.cs:894-993`）先算 `surface = Bounds(this, root)`；`Bounds`（`:1024-1036`）用 `ActualWidth/ActualHeight`。**侧栏 `Visibility.Collapsed` 时层尺寸为 0**，`Apply` 在 `:908-911` 直接 return。
- 因此侧栏折叠期间，模糊层**从未拿到过图像**：`_brush.ImageSource` 为 null，`HasFrame` 永远 false（`HasFrame` 只在 `Visibility == Visible` 的成功 `Apply` 里置位，`:988-991`）。
- 展开时 `SidebarHost.PlayShow`（`SidebarHost.xaml.cs:359-382`）只是把 `Visibility` 置为 `Visible`，`ApplyBlurVisuals`（`:113-126`）把空层设为可见；`SidebarBlurTintLayer` 因 `HasFrame == false` 仍折叠 → 露出底层不透明 `ChromeSolidBrush`。
- 直到下一次抓帧（`Dynamic` 最多 250 ms；`Fixed` 更久）层才真正有图。**`Fixed` 档最坏**：`_fixedCaptured` 可能已经在侧栏折叠时置位，若期间 chrome 一直可见、没有 `SetActive(true)` 重置，侧栏会一直停在纯色底，直到下次隐藏/显示整个 chrome。
- 这也解释了为什么纯色档不闪（本来就是纯色）、为什么不是每次复现（层可能已经因为窗口尺寸变化或模式切换拿到过一帧）。

### 3.3 修复方案

1. `ChromeBlur` 记住最近一次成功发布的 `_bitmap` 与 `VideoGeometry`；新增 UI 线程 API：
   ```csharp
   public void Reapply(ChromeBlurLayer layer) // 只重放，不抓帧
   ```
   用当前 surface 布局把最近一帧立即画进该层（`layer.Apply(_bitmap, _lastGeometry)`）。
2. `SidebarHost` 在**变为可见并完成布局后**调用 `ChromeBlur.Current.Reapply(_blur)`：
   - `PlayShow` / `ShowInstant` 末尾 `DispatcherQueue.TryEnqueue(RefreshBlurFrame)`；
   - `RootGrid.SizeChanged`（或 `PanelBorder.SizeChanged`）在 `live && _blur != null` 时也调用，覆盖首次布局、DPI 变化、侧栏宽度拖动。
3. `Reapply` 时若 `_bitmap == null`（从未抓到过帧）：把 `_fixedCaptured` 复位并请求一次抓帧，保证下一帧能补上；不要无限等待。
4. `ChromeBlurLayer`：成功 `Apply` 后即使层当前不可见也把 `HasFrame` 置位（图层有了图就应记住），但 `SidebarBlurTintLayer` 仍由 `ApplyBlurVisuals` 控制，避免未绘制就显示 tint。
5. 可选：`ApplyBlurVisuals` 在 `_blur.HasFrame` 从 false→true 时再显示 tint（已有 `FirstFrame` 事件，确认它能在 `Reapply` 后触发）。

### 3.4 验证

- 浅色/深色 × 固定/动态/始终/纯色 共 8 组；每组「首次打开 Wiina → 直接展开侧栏」与「先播一会儿再展开」各 20 次，逐帧抓取或 60 fps 录屏，确认侧栏矩形内没有整块 `#F3F3F3` / `#202227`。
- 重点回归 `Fixed` 档：播放中展开侧栏、关闭侧栏、再次展开，确认第二次不再纯色。
- 侧栏宽度拖动、窗口 resize、DPI 变化后模糊仍然贴合视频。
- 纯色档行为不变（仍是不透明纯色）。

---

## 4. Bug 3：默认快捷键绑定了系统音量键（P0）

### 4.1 根因

- `src/Wiina.Core/Input/wiina-defaults.conf:118-120`：
  ```
  VOLUME_UP add volume 2
  VOLUME_DOWN add volume -2
  MUTE cycle mute
  ```
- `WiinaInputDefaults.Parse` 把它们解析成默认 Wiina 方案的绑定；`KeyChordMapper.cs:44-48` 把 `VK_VOLUME_UP/DOWN`（175/174）映射为 `MediaVolumeUp/Down`。
- 结果：Wiina 聚焦时按音量键，**Windows 先改系统音量**，应用又执行 `add volume`，双重生效；用户感知就是「这个快捷键在控制系统音量」。

### 4.2 修复方案

1. 从 `wiina-defaults.conf` 删除 `VOLUME_UP` / `VOLUME_DOWN`（建议连 `MUTE` 一起删；`m cycle mute`、`Ctrl+/ cycle mute` 仍在，功能不丢）。
2. 同步 `resources/mpvnet/wiina-input.conf:116-118`（这是 Wiina 默认键位的导出副本，必须保持一致）。
3. 文档同步：
   - `README.md:100`「79 个常用键位」本来就不准（实际默认 84 条），删 3 条后应为 **81**；顺手修正。
   - `docs/reference/iina-shortcuts.md:96` 的媒体键表格去掉 VOLUME_UP/DOWN/MUTE，并注明「音量键交给 Windows」。
   - `docs/release/notes.md` / `docs/acceptance/v1.3.10.md` 记录行为变化。
4. 新增测试（`KeyBindingTests`）：
   - 默认方案不包含 `MediaVolumeUp/MediaVolumeDown`（以及 `MediaMute`，若采纳）；
   - `PlayerActionCatalog` 仍保留 `VolumeUp2/VolumeDown2`（mpv 方案 `0`/`9` 还在用，不能删动作）；
   - 默认绑定总数断言（建议断言「删除后数量」而不是硬编码，避免下次改键位又过期）。
5. **不主动清理用户自定义方案**：只改内置 Wiina 方案；`KeyBindingProfile` 为「自定义」的用户如果自己加过，保留其数据。

### 4.3 验证

- 键位编辑器打开：默认方案里搜「音量」只剩 `↑/↓`、`Alt+↑/↓`（以及 mpv 方案的 `0/9`）。
- 按键盘音量键：只改 Windows 音量，播放器音量不再跳。
- `↑/↓` 仍改播放器音量（`add volume 5`）。
- 重置默认、导入导出、冲突检测不受影响。

---

## 5. Bug 4：播放时阻止熄屏/锁定/睡眠（P1）

### 5.1 现状

全仓 `grep SetThreadExecutionState|PowerCreateRequest|ES_DISPLAY_REQUIRED` = **0 命中**。播放中完全依赖系统电源计划，长视频会按「屏幕超时」熄屏，随后进入睡眠/锁屏。

### 5.2 方案

新增 `src/Wiina.App/Interop/PowerKeepAwake.cs`（或 `Services/`）：

- 优先 `PowerCreateRequest` + `PowerSetRequest`，同时申请：
  - `PowerRequestDisplayRequired`（阻止熄屏/屏保 → 间接阻止自动锁屏）
  - `PowerRequestSystemRequired`（阻止系统睡眠）
  - `REASON_CONTEXT` 用简单原因字符串 `"Wiina is playing media"`，便于 `powercfg /requests` 排查。
- 失败时回退 `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)`。
- 幂等 `Set(bool)`：重复调用不重复申请；释放时 `PowerClearRequest` + `CloseHandle`。
- 只在「有媒体且未暂停」时持有；暂停、停止、切换文件间隙、窗口关闭都释放。
- 进入 PiP/迷你继续持有（仍在播放）；锁屏/注销/关机不受影响。

设置项：

- `AppSettings.PreventSleepWhilePlaying`（默认 `true`）。
- `SettingsCatalog.cs` 的「界面 → 窗口与 OSC」（或新增「播放与电源」分节）加 `ui.prevent-sleep`：
  - 标题：播放时阻止屏幕休眠
  - 说明：播放中阻止屏幕关闭、系统睡眠与自动锁屏；暂停或退出后恢复。不会修改系统电源计划。
  - 英文覆盖：`SettingsLocalization.cs` 加一条。
- `MainWindow.Settings.cs` 的 `ApplySettingsRow` 处理 `ui.prevent-sleep`；`OnSharedSettingsChanged` 重新评估。
- 调用点：`MainWindow` 的 play/pause/stop、`ApplyLoadedMedia`、`OnClosed`、模式切换（PiP/Mini 不释放）。
- 多窗口：每个窗口持有自己的 request，互不影响；关闭一个窗口只释放自己的。

### 5.3 边界（写进 known-issues）

- 不修改系统电源计划，不阻止用户手动锁屏，不阻止关机/注销。
- 只在 Wiina 播放中生效；暂停即释放（IINA 同款行为）。
- 若有策略强制「无人参与超时休眠」，应用层请求可能被组策略覆盖，需在文档说明。

### 5.4 验证

- `powercfg /requests`：播放中出现 `DISPLAY` / `SYSTEM` 两项且进程为 Wiina；暂停后消失；关闭窗口后消失。
- 把系统「关闭屏幕」临时设为 1 分钟，播放 5 分钟不熄屏；暂停后 1 分钟内正常熄屏。
- 休眠唤醒后播放不黑屏（与 Bug 5 联动验证）。

---

## 6. Bug 5：长时间播放概率黑屏（mpv 视频输出停住）（P0）

### 6.1 已知证据

- `docs/reports/hang-analysis-1.2.7.md`：trace 里 libplacebo `Requested PTS 0.000000 is lower than the oldest frame PTS` 与 `screenshot-raw` 的 125 ms 周期同步出现；随后 `DXGI_ERROR_DEVICE_REMOVED 0x887A0005` 反复刷屏。
- `docs/reports/ui-freeze-analysis.md`：8 Hz 全帧 GPU→CPU 回读是「纯浪费，且是卡死放大器」。
- 1.3.2 已加 `DeviceLoss` 恢复，但只在 mpv 日志出现设备丢失字样、或绑定 swapchain 抛 HRESULT 时触发。

### 6.2 当前代码缺口

1. **抓帧循环无暂停跳过、无失败退避**：`Interop/ChromeBlur.cs:294-353`，每 125 ms 无条件 `ScreenshotRaw()`；`_active` 只看 chrome 可见性，暂停时照抓；连续失败会一直重试。
2. **每帧新建 GPU 资源**：`ChromeBlur.cs:540-546` 每帧 `new CanvasRenderTarget` + `GetPixelBytes()` + `Downscale` 新数组；8 Hz × 数小时 = 大量 GPU 资源申请/释放与 GC 压力。
3. **恢复开关粘滞**：`MainWindow.xaml.cs:2531-2540` `_deviceRecoveryFailed = true` 之后**永不重置**；一次连续 3 次恢复失败后，本进程后续所有设备丢失都不再尝试恢复。
4. **恢复后毛玻璃不恢复**：`RecoverFromDeviceLoss` 先 `ChromeBlur.SetActive(false)`（`:2453`），`CompleteDeviceRecovery`（`:2515-2527`）只 `SetMode` 不重新 `SetActive(true)`，模糊层会一直停在最后一帧。
5. **无「画面停了」看门狗**：如果 mpv 不再 present、但没打设备丢失日志，应用不会做任何事；`time-pos` 还在走（音频继续），画面永久黑。
6. **无睡眠/显示变化处理**：全仓无 `WM_POWERBROADCAST` / `WM_DISPLAYCHANGE`；显示器休眠/唤醒、分辨率变化后 composition swapchain 可能失效，没有重新布局/重载。
7. **同 handle 不重绑**：`OnDisplaySwapChainChanged`（`:2376-2412`）在 `swapChain == _swapChain` 时直接 return；恢复期间若 mpv 复用同一 handle，恢复会 5 s 超时失败。

### 6.3 修复方案（分层，尽量低风险）

**A. 抓帧治理（`ChromeBlur.cs`）**

- `pause=yes` 时跳过抓帧（Fixed 模式若尚未抓到首帧则允许一次）。
- 间隔 125 → **250 ms**（4 Hz，肉眼几乎无差，GPU 回读减半）；常量保留可调。
- 连续失败计数：1 次正常日志、3 次开始指数退避（250 ms → 2 s → 5 s），成功即复位；避免设备丢失时 8 Hz 刷屏。
- 复用 `CanvasRenderTarget`（尺寸变化才重建）与 downscale 缓冲；减少每帧分配。
- `ChromeBlurLayer.UpdateClip` 只挂 `SizeChanged`，复用 `RectangleGeometry`（§7.5）。

**B. 渲染看门狗（新 `MainWindow.Watchdog.cs` 或 `Stats` 旁）**

- 2 s 定时器：读 `time-pos`、`estimated-frame-number`、`pause`、`seek` 状态。
- 判定「正在播放（!pause）且 `time-pos` 前进 ≥3 s，但 `estimated-frame-number` 没增长」→ 触发一次 `RecoverFromDeviceLoss("no frames presented")`。
- 独立预算（比设备丢失更保守：10 分钟内最多 2 次）；第二次仍停 → OSD 提示「渲染器已重置，请重新打开文件」，停止自动重试。
- 避免误报：seek/换文件/缓冲/暂停期间不计；两次采样都要满足。

**C. 恢复路径修正（`MainWindow.xaml.cs`）**

- 打开新文件/用户手动重载时重置 `_deviceRecoveryFailed` 与 `DeviceLossRecoveryBudget`。
- `CompleteDeviceRecovery` 里按当前 chrome 可见性恢复 `ChromeBlur.SetActive(...)`。
- 恢复期间允许同 handle 重绑（`_deviceRecoveryPending` 时不做 equal-handle 早退）。
- 恢复超时 5 s → 8 s；失败后保留手动「重新加载视频输出」菜单项。

**D. 电源/显示变化（`WindowInterop.Hook`）**

- 在现有 `SetWindowSubclass` 的 `WndProc` 里加：
  - `WM_POWERBROADCAST`：`PBT_APMRESUMEAUTOMATIC` / `PBT_APMRESUMESUSPEND` → `ApplyVideoLayout()` + 若 panel 无新帧则 `video-reload`。
  - `WM_DISPLAYCHANGE` / `WM_DPICHANGED` → 重新 `ApplyVideoLayout()`，必要时 `video-reload`。
- 回调通过 `WindowInterop.Attach` 新增可选参数传入，保持 PiP 窗口不挂这套逻辑（PiP 自己管理）。

**E. 诊断埋点（默认轻量，便于下次定位）**

- trace 记录：`blur capture failed (n)`、`watchdog: pos=… frame=…`、`power resume`、`display change`、`video-reload requested/recovered`。
- 可选 `WIINA_BLUR_LOG=1` 已有；不新增默认刷屏日志。

### 6.4 诚实说明

开发机无法稳定复现「连续播放数小时黑屏」。本项是**机制性修复 + 看门狗兜底**，不是拿到线程栈后的定论。验收以「长跑不再黑屏 / 黑屏能自动恢复 / trace 可解释」为准；如果修复后仍复现，需要按 `hang-analysis-1.2.7.md` §4 抓 procdump。

### 6.5 验证

- 4 小时连续播放（1080p HEVC），每 15 分钟记录一次 `time-pos` / `estimated-frame-number` / 进程 GPU 内存。
- 手动熄屏（`powercfg` 超时 1 分钟）→ 唤醒 → 画面恢复，trace 有 `power resume`。
- 人为制造恢复失败（可选：调试开关让 `video-reload` 抛错）→ 确认不会永久关闭恢复。
- 暂停 30 分钟：抓帧停止（`WIINA_BLUR_LOG=1` 无 capture 行），CPU 明显下降。

---

## 7. 检测中额外发现（建议一并修）

| # | 问题 | 位置 | 影响 | 处理 |
|---|---|---|---|---|
| 7.1 | **恢复后毛玻璃永久停摆**：`SetActive(false)` 后 `CompleteDeviceRecovery` 只 `SetMode`，不恢复 `_active` | `MainWindow.xaml.cs:2453`、`:2515-2527` | 设备恢复后标题栏/OSC 一直是旧帧 | 归入 Bug 5-C，修 |
| 7.2 | **`_deviceRecoveryFailed` 粘滞**：一次预算耗尽后本进程永不恢复 | `MainWindow.xaml.cs:2531-2540` | 后续偶发 TDR 直接黑屏 | 归入 Bug 5-C，修 |
| 7.3 | **列表刷新丢选中/滚动**：`SetItems` 每次 `Clear()` 重建，移动后选中丢失、列表回顶 | `Controls/PlaylistView.xaml.cs:44-67`、`:385-400` | 连续下移要反复重选；长列表体验差 | 按 `Path` 记录并恢复选中，恢复滚动偏移；归入 Bug 1 |
| 7.4 | **同 swapchain handle 不重绑**：恢复期间 early-return | `MainWindow.xaml.cs:2380` | 恢复 5 s 超时误判失败 | 归入 Bug 5-C |
| 7.5 | **`ChromeBlurLayer.UpdateClip` 挂 `LayoutUpdated`**：每次布局新建 `RectangleGeometry` 并改布局属性 | `Interop/ChromeBlur.cs:845-872` | 长时间播放 GC/合成抖动；`ui-freeze-analysis.md` P2-2 | 归入 Bug 5-A，改 `SizeChanged` + 复用几何 |
| 7.6 | **列表首尾按钮无禁用态**：第一项的上移、最后一项的下移仍可点，静默无反应 | `PlaylistView.xaml`、`.xaml.cs:205-220` | 用户以为按钮坏了（与本 bug 混淆） | 按选中项/位置切换 `IsEnabled`，归入 Bug 1 |
| 7.7 | **多选只移动第一项** | `PlaylistView.xaml.cs:205-220` | 多选下移只动一条 | 明确文档化「只移动首项」，或按选中集合整体移动；本版先文档化 |
| 7.8 | **文档计数过期**：README 写 79 个默认键位（实际 84）；1.3.9 验收写 133 行/581 键，新增设置行后需同步 | `README.md:100`、`docs/acceptance/v1.3.9.md`、`docs/reference/settings-layout.md` | 文档与实现不一致 | 1.3.10 文档统一更新 |
| 7.9 | **`bin/Release` 是 1.0.2 旧产物** | `src/Wiina.App/bin/Release/...` | 误启动会得到旧行为，曾导致定位偏差 | 不提交 bin；文档注明用 `bin/x64/Release` 或 `artifacts/`（`.gitignore` 已忽略） |

> 说明：7.1/7.2/7.4 不是「顺便重构」，而是长时间黑屏恢复链的一部分，建议同批修。

---

## 8. 子代理分工与写入范围（拟）

> 目标：并行推进，文件级写入范围尽量不重叠；重复文件用依赖/顺序解决。

| 组 | 负责 | 写入范围（advisory） | 依赖 |
|---|---|---|---|
| A | Bug 1 + 7.3/7.6/7.7：播放列表下移、选中保持、首尾禁用、测试 | `src/Wiina.App/Controls/PlaylistView.xaml`、`PlaylistView.xaml.cs`、`src/Wiina.App/MainWindow.Sidebar.cs`、`src/Wiina.Core/Files/PlaylistMove.cs`（新）、`tests/Wiina.Core.Tests/PlaylistMoveTests.cs`（新） | 无 |
| B | Bug 2：侧栏快速设置毛玻璃首帧闪白/黑底（`Reapply` + `SidebarHost` 布局后重放） | `src/Wiina.App/Interop/ChromeBlur.cs`（仅新增 Reapply/缓存最近帧）、`src/Wiina.App/Controls/SidebarHost.xaml.cs` | 无 |
| C | Bug 3 + Bug 4：默认键位、防休眠服务、设置行、测试 | `src/Wiina.Core/Input/wiina-defaults.conf`、`resources/mpvnet/wiina-input.conf`、`src/Wiina.App/Interop/PowerKeepAwake.cs`（新）、`src/Wiina.App/MainWindow.Power.cs`（新 partial）、`src/Wiina.App/MainWindow.Modes.cs`（仅初始化/电源挂钩）、`src/Wiina.App/MainWindow.Settings.cs`、`src/Wiina.Core/Settings/AppSettings.cs`、`SettingsCatalog.cs`、`SettingsLocalization.cs`、`tests/Wiina.Core.Tests/KeyBindingTests.cs` | 无 |
| D | Bug 5 + 7.1/7.2/7.4/7.5：抓帧治理、看门狗、恢复修正、电源/显示消息 | `src/Wiina.App/Interop/ChromeBlur.cs`、`src/Wiina.App/Interop/WindowInterop.cs`、`src/Wiina.App/MainWindow.xaml.cs`、`src/Wiina.App/MainWindow.Watchdog.cs`（新 partial）、`src/Wiina.Core/Playback/RenderWatchdog.cs`（新纯逻辑） + 测试 | **blocked_by B**（都要改 `ChromeBlur.cs`，B 先落地） |
| E | Lead：合并、冲突、文档、版本、构建、PR | `docs/**`、`Directory.Build.props`、`src/Wiina.Core/Branding.cs`、`PackageManifest.cs`、`app.manifest`、`installer/wiina.iss`、`scripts/package.ps1`、`README.md` | 全部完成 |

冲突预案：

- B 与 D 都碰 `ChromeBlur.cs`；用 `blocked_by B` 串行，D rebase 后再改。
- C 不碰 `MainWindow.xaml.cs`：防休眠在 `MainWindow.Power.cs`（新 partial）里完成初始化，通过 `AppWindow.Closing` / `_oscVm.PropertyChanged` 驱动，关闭时自动释放，避免和 D 抢同一文件。
- A 独立；B 不再需要改 `PlaylistView.xaml.cs`。
- 任何组不得修改 `main`、不得动 `artifacts/`、不得改用户设置文件、不得改 `docs/`（Lead 统一写）。

---

## 9. 交付流程（1.3.10）

1. **分支**：`feat/v1.3.10`（从 `main` 切出），不合并 `main`。
2. **实现 + 测试**：按 §8 分组；每组自测，Lead 统一 review diff。
3. **构建门禁**：
   ```powershell
   $dotnet = 'C:\Users\zheng\.dotnet\dotnet.exe'
   & $dotnet build .\src\Wiina.App\Wiina.App.csproj -c Release -p:Platform=x64
   & $dotnet test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Release
   ```
   要求 Release 0 警告 / 0 错误；测试 ≥ 943 且新增用例全绿。
4. **版本同步**：`pwsh -File scripts/release.ps1 -Version 1.3.10`（自动写 `Directory.Build.props` / `Branding.Version` / `PackageManifest` / `app.manifest` / `package.ps1` / `wiina.iss`）。
5. **产物**：`artifacts/wiina-1.3.10-win-x64.zip` + `artifacts/Wiina-Setup-1.3.10-x64.exe`；记录字节数与 SHA-256。
6. **文档**：
   - 新增 `docs/acceptance/v1.3.10.md`（逐项问题、证据、截图、产物哈希）。
   - 更新 `docs/release/notes.md`、`docs/release/known-issues.md`（标题版本、防休眠边界、长播看门狗说明）。
   - 更新 `docs/reference/settings-layout.md`（新增 1 行、行数/键数）。
   - 更新 `docs/reference/iina-shortcuts.md`、`README.md`（键位数量、测试数）。
   - 本文件追加「实施记录 / 新增发现」。
7. **PR**：
   - `git push -u origin feat/v1.3.10`。
   - `gh` 未安装：优先用 `git credential fill` 拿 token 走 REST（`POST /repos/RareSpecies233/wiina/pulls`，`base=main`，`head=feat/v1.3.10`，`draft=true`）；拿不到 token 就输出 compare URL 请你点「Create pull request」。
   - PR 描述附：问题清单、根因、测试命令/结果、截图、产物哈希、未解决风险。
   - **不合并**。
8. **验收冒烟（computer use）**：
   - 播放列表：选中第 1 项连点下移两次、拖拽向下、首尾按钮禁用态。
   - 毛玻璃：浅/深 × 四档，展开**侧栏快速设置** 20 次，逐帧截图无纯白/纯黑；重点回归 Fixed 档。
   - 快捷键：编辑器默认方案无音量键；按媒体音量键只改系统音量。
   - 防休眠：`powercfg /requests` + 1 分钟熄屏设置实测。
   - 长播：至少 1 小时连续播放 + 暂停/恢复 + 熄屏唤醒。

---

## 10. 已确认的决定（2026-11-07）

1. **「OSC 快速菜单」= 侧栏「快速设置」**（Layout / Video / Audio / Subtitles）。Bug 2 按 §3 的侧栏首帧方案修，不按 `⋯` 菜单方案。
2. **音量键三个都删**：`VOLUME_UP`、`VOLUME_DOWN`、`MUTE`；`M` / `Ctrl+/` 仍可静音。
3. **防休眠默认开启**（`PreventSleepWhilePlaying = true`）。
4. **长播黑屏口径**：接受「抓帧治理 + 看门狗自动 `video-reload` + 恢复链修正」；后续复发再抓转储深入。
5. **PR 目标 `main`，保持未合并**；文档按仓库既有版本记录方式同步（验收页 / notes / known-issues / 设置布局 / 快捷键参考 / README）。
6. **清理过期二进制**：已删除 `src/**/bin`、`src/**/obj`、`tests/Wiina.Core.Tests/{bin,obj}` 下全部旧产物（含 1.0.2 与 1.3.9 本地构建）；`artifacts/` 保留到 1.3.10 产物生成后再清理旧版本包。

---

## 11. 定位轮的边界（已完成）

- 定位轮未修改任何源码、测试、脚本、版本号；本轮只新增了这份文档。
- 定位轮未提交 commit、未 push、未创建 PR。
- 定位轮使用 1.3.9 产物复现，未改用户 `settings.json`、未改系统电源计划。
- 进入实施轮后：分支 `feat/v1.3.10` 已创建；旧 `bin/obj` 已清理；下方 §12 记录实施结果。

---

## 12. 实施记录（待修复后填写）

- [ ] A 播放列表
- [ ] B 毛玻璃菜单
- [ ] C 快捷键 + 防休眠
- [ ] D 长播稳定性
- [ ] E 文档 + 版本 + 构建 + PR
- 新增发现：
