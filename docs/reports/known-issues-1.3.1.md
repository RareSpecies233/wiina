# Wiina 已知问题清单（v1.3.1，供用户决定下一轮修什么）

> 快照：2026-10-24，分支 `feat/v1.3.1`（工作树，尚未提交）。
> 本文是**决策清单**，不是发布说明：每条都给出可观察症状、根因、当前绕法、建议修法与大致成本，
> 最后一列留给用户勾选。成本是工程估算（人天），不是承诺。
> 相关分析：`docs/reports/hang-analysis-1.2.7.md`（挂起/崩溃）、`docs/reports/ui-freeze-analysis.md`（界面冻结）。
> 严重度：**P0** = 崩溃/挂起/数据风险；**P1** = 明显影响体验或性能；**P2** = 小问题、债务或取舍。

## 0. 本轮（1.3.1）已实现、待实机确认的项

| 项 | 状态 | 备注 |
| --- | --- | --- |
| 透明光标（item 3） | **部分实现，未实机确认** | 资源 `RT_GROUP_CURSOR 101`（`Wiina.Core.dll`）、`RootGrid` 下 489 个元素统一 `ProtectedCursor`、60 ms 辅助线程 `SetCursor(NULL)` 均已接入；但未观察到稳定隐藏效果——CoreMessaging 会在毫秒级把箭头贴回。**不要当作已修复**，见 K26。 |
| 缩略图 GPU 生成（item 8） | 已实现 | 自动（推荐）/仅 CPU 两档；自动走 `d3d11va`，CPU 档 `hwdec=no`。 |
| 内存缩略图缓存改为全片（m04084） | 已实现 | 缓存当前视频全部时间点，约 57.6 KB / 5 s 桶。 |
| 截图是否包含字幕（item 9） | 已实现 | 设置 → 视频 → 截图、OSC 展开菜单、快速设置 → 视频**三处**开关，读写同一个 `AppSettings.ScreenshotWithSubtitles`。 |
| 卸载器提权（item 7） | 已实现 | 安装包仍按用户安装（不需要管理员）；**卸载器** manifest 为 `requireAdministrator`，始终提权，清理 HKCU+HKLM 关联。 |
| OSC 速度档位（item 4，按 m04030 澄清） | 已实现 | 仅放在 OSC 展开菜单 `⋯ → 置顶 → 速度 ▸`；快速菜单首页不再放，播放页保留滑杆。 |
| 菜单去重（item 5） | 已实现 | 视频/窗口重复项只保留窗口版本，并带动态快捷键提示。 |
| 指针移出立即隐藏（item 6） | 已实现 | `PointerExited` 确认光标离开窗口即隐藏，不再等 2.5 s；flyout 打开、左键按住或侧栏可见时延后到弹出层关闭。 |
| 上一首/下一首可改快退/快进（item 2） | 已实现 | 设置 → 界面 → 窗口与 OSC，默认上一首/下一首，可改为 ±5 秒（Ctrl = ±30 秒）。 |
| IINA 预设移除（item 1） | 已实现 | 只保留 Wiina（来自 `wiina.txt`）与 mpv 两套；旧 `IINA` 值迁移到 Wiina；Wiina 增加 `Ctrl+,` 打开设置。 |
| OSC 速度显示只在右侧（item 2 前半） | 已实现 | 后退/前进按钮两侧不再重复显示速度。 |

## 1. P0

| ID | 严重度 | 区域 | 症状（用户可见） | 根因 | 现状 / 临时绕法 | 建议修法（成本） | 涉及文件 | 下一轮修？ |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| K01 | P0 | 关闭/挂起 | 播放中关窗偶发“未响应”，约 26 s 后被系统结束（AppHangB1，1.2.5/1.2.6 均有记录） | 历史根因：关闭竞态——8 Hz 取帧线程仍在调 mpv，而 `_player.Dispose()` 已进入 `mpv_terminate_destroy`；同时无设备丢失恢复 | **部分已修（1.2.7）**：`OnClosed` 在 `VideoHost.Detach()`/`_player.Dispose()` 之前调用 `ChromeBlur.SetActive(false)` + `ChromeBlur.Shutdown()`（`Join(1s)`）；`MpvPlayer.Dispose()` 自身取消并 Join 事件线程（≤3 s）。仍无“关闭中”看门狗、无 staging 纹理显式 flush。绕法：关窗前先暂停 | 剩余项：关闭进度看门狗 + 关闭步骤 Trace + 压测（1–2 天，高风险） | `src/Wiina.App/MainWindow.xaml.cs`（`OnClosed` 2690+，调用 2727-2728）、`src/Wiina.App/Interop/ChromeBlur.cs:83`、`src/Wiina.Mpv/MpvPlayer.cs:965` | [ ] |
| K02 | P0 | 渲染/设备丢失 | 驱动升级、休眠唤醒、切换独显/核显后画面黑掉、花屏或停止刷新，只能重开 | 全库没有 `DXGI_ERROR_DEVICE_REMOVED` / `DEVICE_LOST` 处理（0 命中）；swapchain 只在 mpv 重新发布时重绑 | 未修。绕法：重开文件或重启应用 | 注册设备丢失回调 → 释放旧 `CanvasDevice`/swapchain → 重建并让 mpv 重新输出；补 UI 提示（1–2 天，中高风险） | `src/Wiina.App/Controls/MpvVideoView.xaml.cs`、`src/Wiina.App/Interop/SwapChainPanelInterop.cs`、`src/Wiina.App/MainWindow.xaml.cs`（重绑段 1902–1928） | [ ] |

## 2. P1

| ID | 严重度 | 区域 | 症状 | 根因 | 现状 / 绕法 | 建议修法（成本） | 涉及文件 | 下一轮修？ |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| K03 | P1 | 毛玻璃性能 | 播放时 CPU 占用偏高、笔记本发热；窗口大时更明显 | 三档玻璃按 125 ms（≈8 Hz）整帧 `screenshot-raw` 回读，320 px 缩放后 Win2D 高斯；1280×720 约 45 ms / 3.7 MB 每帧，≈1/3 核 | 设置 → 界面 → 关闭毛玻璃（纯色，零开销）；或“固定（静态帧）” | 暂停/降频（不可见时停、250 ms）、按窗口尺寸自适应、回读区域裁剪（1–2 天） | `src/Wiina.App/Interop/ChromeBlur.cs` | [ ] |
| K04 | P1 | 统计面板 | 打开统计时偶发轻微卡顿；面板每秒刷新 | 每秒在 UI 线程同步读取 30+ 个 mpv 属性（`GetProperty*`） | 关掉统计面板；用“丢帧提示”开关替代 | 改为 mpv 属性观察/事件缓存，UI 只读缓存（1–2 天） | `src/Wiina.App/MainWindow.Stats.cs` | [ ] |
| K05 | P1 | 缩略图（网络） | 打开 SMB/HTTP 上的视频，进度条预览每次都要重新生成，拖回去要等 | 磁盘缓存/预生成只对本地文件（`IsLocalFile` 关卡），网络路径只走内存 | 无 | 为网络源提供可选磁盘缓存或跳过预览的提示（1 天） | `src/Wiina.App/Services/ThumbnailService.cs:1140` | [ ] |
| K06 | P1 | 缩略图（悬停） | 快速来回扫进度条时，偶发 seek 卡顿/预览排队 | 悬停按 5 s 桶触发 headless mpv seek+screenshot，仅做同桶去重，没有时间节流 | 慢慢移动鼠标 | 悬停节流（如 120–200 ms）+ 取消过期请求（0.5–1 天） | `src/Wiina.App/Services/ThumbnailService.cs`、进度条控件 | [ ] |
| K07 | P1 | 缩略图（磁盘） | 删掉/改名/移动视频后，缩略图目录不会自动变小 | 缓存键含路径+mtime+大小；旧条目只按 LRU 预算淘汰，无文件存在性清理 | 设置里“清理缩略图缓存” | 启动或按需扫描孤儿条目（0.5 天） | `src/Wiina.Core/Caching/ThumbnailDiskCache.cs` | [ ] |
| K08 | P1 | 贴边吸附恢复 | 最大化/贴边后拖动还原，偶发还原到错误显示器或偏移 1 px（混合 DPI） | 还原点取“光标所在显示器”；DWM 边框在混合 DPI 下补偿不完全 | 手动挪一下 | 记住还原前显示器与 DPI，按工作区换算（1 天） | `src/Wiina.Core/State/WindowSizing.cs`、`src/Wiina.Core/State/EdgeSnap.cs`、`src/Wiina.App/MainWindow.xaml.cs` | [ ] |
| K09 | P1 | 快捷键 | 设置里没有“快捷键”页（目录里有 KeyBindings 页但侧栏不显示），只能从“控制”里的按钮打开编辑器 | 1.3.0 重排时保留页面枚举、侧栏跳过；编辑器独立窗口 | 设置 → 控制 → 打开快捷键编辑器 | 二选一：删掉死页枚举，或恢复为可导航页（0.5 天） | `src/Wiina.Core/Settings/SettingsCatalog.cs`、`src/Wiina.App/Views/SettingsWindow.xaml.cs:114` | [ ] |
| K10 | P1 | 安装/卸载 | 卸载时若 Wiina 正在运行，会提示是否强制结束；否则可能残留文件 | 卸载器无法安全关闭正在播放的进程 | 按提示选择“强制结束” | 已按用户要求加提示；可选：先尝试正常关闭再强制（0.5 天） | `installer/wiina.iss`、`src/Wiina.Uninstall/Program.cs` | [ ] |
| K11 | P1 | 安装/卸载 | 安装包按用户安装（不弹 UAC），便携卸载器始终要 UAC；安装版卸载器只在仍有 HKLM 关联时提一次 | 设计如此：setup `PrivilegesRequired=lowest`；便携卸载器 `requireAdministrator`，安装版按需提权清理 HKLM 关联 | 接受 UAC | 文档已说明；若希望安装也提权，需要改 per-machine 模型（1 天，破坏免管理员安装） | `installer/wiina.iss`、`src/Wiina.Uninstall/app.manifest` | [ ] |

## 3. P2

| ID | 严重度 | 区域 | 症状 | 根因 | 现状 / 绕法 | 建议修法（成本） | 涉及文件 | 下一轮修？ |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| K12 | P2 | 高对比度 | 切换高对比度/主题时，Mica 窗口可能有一帧旧底色；HC 下回退纯色 | `WindowShell.TryUseMica` 在主题切换后重设 backdrop，首帧未重绘 | 切换主题后重开窗口 | 主题变化时先设纯色再恢复 Mica（0.5 天） | `src/Wiina.App/Services/WindowShell.cs:162` | [ ] |
| K13 | P2 | 文件关联 | 设为默认播放器仍要在“默认应用”里确认；脚本改 `UserChoice` 会被 Windows 重置 | Windows 10/11 保护 `UserChoice` 哈希 | 手动确认一次 | 无（系统限制），可在 UI 里解释更清楚（0.5 天） | `src/Wiina.App/Services/FileAssociationService.cs` | [ ] |
| K14 | P2 | 设置界面 | 控制页与（隐藏的）按键绑定页共用同一图标 `\uE765` | 图标复用 | 无 | 换图标（0.1 天） | `src/Wiina.Core/Settings/SettingsCatalog.cs` | [ ] |
| K15 | P2 | 设置界面 | 统计面板的行标签/分组名只有中文 | 标签硬编码在 `MainWindow.Stats.cs` | 无 | 抽到 `UiStringCatalog`（1 天） | `src/Wiina.App/MainWindow.Stats.cs` | [ ] |
| K16 | P2 | 播放 | 播放位置事件（`time-pos`，约 60/s）逐条跨线程编组到 UI | 事件直连 UI 线程更新 | 无 | 合帧/降频到 10–20 Hz（0.5–1 天） | `src/Wiina.App/MainWindow.xaml.cs:1510`、`MpvPlayer` 事件 | [ ] |
| K17 | P2 | 播放 | 大窗口 + 毛玻璃时，布局变化会重建裁剪矩形，偶发掉帧 | `ChromeBlurLayer` 在 `LayoutUpdated` 里 `new RectangleGeometry` | 关闭毛玻璃 | 复用几何对象、限制重算频率（0.5 天） | `src/Wiina.App/Interop/ChromeBlur.cs:530-553` | [ ] |
| K18 | P2 | 缩略图内存 | 长视频（2 h）缩略图内存可达约 83 MB（全片缓存后） | 1.3.1 按 m04084 改为缓存全片，约 57.6 KB / 5 s 桶 ≈ 41 MB/小时 | 设“缩略图缓存下限”或关闭预览；磁盘上限可调 | 提供“仅当前窗口/整片”二选一，或按内存压力淘汰（1 天） | `src/Wiina.App/Services/ThumbnailService.cs`、`src/Wiina.Core/Caching/ThumbnailDiskCache.cs` | [ ] |
| K19 | P2 | 截图 | ~~截图开关只有两处、容易不一致~~ **1.3.1 已补齐** | 1.3.1 之前只有设置和 OSC 展开菜单两处入口 | 设置里关掉 | 已增加“快速设置 → 视频 → 截图包含字幕”，三处读写同一个 `AppSettings.ScreenshotWithSubtitles` 并通过设置变更同步 | `src/Wiina.Core/Settings/AppSettings.cs`、`MainWindow.Playback.cs:344/1033`、`MainWindow.Menu.cs:58`、`QuickSettingsView.xaml` | [x] |
| K20 | P2 | 快捷键 | 移除 IINA 预设后，旧配置里的 `IINA` 会迁移到 Wiina，键位可能与预期不同 | `KeyBindingProfiles.Normalize` 迁移策略 | 重新选 mpv 或自定义 | 首次迁移时提示（0.5 天） | `src/Wiina.Core/Input/KeyBindings.cs` | [ ] |
| K21 | P2 | 设置界面 | 设置 11 页中视频页 38 行、界面页 19 行，仍偏重；没有“每页恢复默认” | 历史迁移 | 用整页重置按钮 | 增加分节级重置（1 天） | `src/Wiina.Core/Settings/SettingsCatalog.cs`、`SettingsWindow.xaml.cs` | [ ] |
| K22 | P2 | 贴边吸附 | 高缩放下 10 DIP 吸附 / 22 DIP 脱离被放大（200% 时 20/44 px），部分用户觉得“太粘” | 阈值按 DPI 缩放 | 设置里关掉贴边 | 阈值改为固定像素或可配置（0.5–1 天） | `src/Wiina.Core/State/EdgeSnap.cs` | [ ] |
| K23 | P2 | 分发 | 未签名，SmartScreen/杀软会拦 | 无代码签名证书 | 阅读 README 的 SmartScreen 说明，校验 SHA-256 | 购买签名证书并接入 CI（取决于证书，1–2 天） | `installer/wiina.iss`、`scripts/package.ps1` | [ ] |
| K24 | P2 | 架构债 | 自绘标题栏/OSC/侧栏与 Win32 子类化并存，部分窗口行为（主题、DPI、焦点）需要各窗口单独适配 | Stage 7 遗留 | 无 | 抽公共窗口基类/统一主题管线（2–3 天） | `src/Wiina.App/Services/WindowShell.cs`、`Interop/WindowInterop.cs` | [ ] |
| K25 | P2 | 本地化 | 检查器/滤镜/统计面板的行名、日志仍为中文；4 条设置行的英文覆盖曾缺失（1.3.1 已补齐 129/129） | 历史硬编码 | 无 | 继续迁移到 `UiStringCatalog`（按页推进，1–2 天） | `MainWindow.Stats.cs`、`Views/*Window.xaml`、`UiStringCatalog.cs` | [ ] |
| K26 | P2 | 光标隐藏 | “隐藏光标”仍可能失败：CoreMessaging 毫秒级恢复箭头；全屏/多屏/Alt+Tab 后更明显 | WinUI 3 桌面窗口光标由 CoreMessaging 驱动；`ProtectedCursor` + 辅助线程 `SetCursor(NULL)` 不足以稳定覆盖 | 无（功能按“部分实现”交付） | 方案 A：挂 CoreMessaging 的 `SetCursor` 导出；方案 B：视频区改 Win32 子窗口承载；二者都需实机验证（2–4 天） | `src/Wiina.App/Interop/WindowInterop.cs:47-67/577/610`、`MainWindow.xaml.cs:852/869/987/997` | [ ] |
| K27 | P2 | 统计面板 | 面板出现/隐藏时与 OSC 自动隐藏偶发抢焦点（统计面板算作 chrome 后已缓解） | 多来源可见性 | 关闭统计面板 | 继续观察（0.5 天） | `src/Wiina.App/MainWindow.xaml.cs`（autohide 段） | [ ] |

| K28 | P1 | 关闭/渲染 | 关闭或切换视频输出后偶发首帧黑屏/花屏，重开窗口恢复 | mpv `d3d11-output-mode=composition` 的 staging 纹理在关闭/换源时没有显式 flush/等待，`VideoHost.Detach()` 直接放掉 swapchain | 重开文件 | 在 Detach 前插入一次 present/flush 并等待 GPU 完成（0.5–1 天，需实机验证） | `src/Wiina.App/Controls/MpvVideoView.xaml.cs`、`src/Wiina.App/Interop/SwapChainPanelInterop.cs` | [ ] |
| K29 | P1 | 关闭/回调 | 极少数情况下关窗瞬间仍有 UI 回调访问已释放的播放器 | 应用层没有逐个摘除 mpv 事件订阅；依赖 `MpvPlayer.Dispose()` 取消 `_cts` 并 Join 事件线程（≤3 s） | 无 | 在 `Dispose` 前显式退订 UI 事件并加“已关闭”断言（0.5–1 天） | `src/Wiina.Mpv/MpvPlayer.cs:965-989`、`src/Wiina.App/MainWindow.xaml.cs`（`OnClosed`） | [ ] |
| K30 | P1 | 稳定性 | 未捕获异常只写 `crash.log`，窗口仍会直接退出；`async void` 异常更隐蔽 | `App.OnUnhandledException` 记录后未设 `e.Handled`；全项目 18 处 `async void` | 查看 `%LOCALAPPDATA%\Wiina\crash.log` | 统一 `async void` 包装为 `SafeFireAndForget`；异常时给出退出提示并可选择继续（1–2 天） | `src/Wiina.App/App.xaml.cs:853-858`、各 `async void` 事件处理器 | [ ] |

## 4. 已在此前版本修复、无需再排期的项

- 依赖动画（`EnableDependentAnimation`）滥用：当前 XAML 0 处。
- 设置英文覆盖缺失：1.3.1 起 129/129 行全部有英文。
- 持久技术 OSD：1.2.4 已移除，只保留统计面板与普通 OSD。
- 640×360 合成上行缩放上限：1.2.7 移除。
- PiP 放大后内容不缩放：1.2.5 修复（`SetLayoutSize` + 裁剪）。
- 快速设置页签在播放列表/章节/插件页点不到：1.2.3 修复。
- 设置左栏悬停变亮：1.2.7 修复（rest == hover）。
- 窗口边缘留白/吸附过强：1.2.5 起 10 DIP 吸附 + 22 DIP 脱离，1.2.7 用 DWM 边框补偿。

## 5. 建议的下一轮优先级

1. **P0**：K01 关闭竞态剩余项、K02 设备丢失恢复——这两条直接对应历史挂起/崩溃报告，建议同批修。
2. **P1**：K03 毛玻璃回读降频、K04 统计同步读、K06 悬停节流、K08 吸附还原——都是低风险、用户可感知；K28/K29/K30 属关闭与稳定性收尾，可与 K01 同批。
3. **P2**：K12/K14/K15/K18/K22 可顺手清理；K23/K24/K26 需要单独排期。

> 勾选方式：在最后一列把 `[ ]` 改成 `[x]` 即可；本轮只做文档，不改代码。
