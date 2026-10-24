# ADR-0004：不透明 accent 染色外框（移除取帧模糊）

- 状态：**Accepted**（2026-10-24，v1.2.3；替代 [ADR-0003](0003-acrylic.md) 中“视频帧高斯模糊”的部分）
- 相关：用户问题 2（纯色模式不应透出内容，并关闭抓帧/模糊渲染）、`ChromeSolidBrush`、`src/Wiina.App/Services/ChromeTint.cs`

## 背景

ADR-0003 的方案是每 125 ms 从 libmpv 取一帧真实视频帧（`screenshot-raw`，`bgr0`），在后台线程
降采样到 320 px 宽，用 Win2D `GaussianBlurEffect` 模糊，再按控件在屏幕上的位置裁切分发给标题栏、
底部/浮动 OSC、侧边栏与 OSD。

它能“看起来像毛玻璃”，但代价与缺陷都很明确：

- **每帧一次全分辨率 GPU 回读**：1280×720 单帧约 45 ms、3.7 MB；720p 下占用大约三分之一个
  CPU 核心，4K 更高。这是持续的、与播放无关的开销。
- **滞后与失真**：外框最多落后视频一帧（125 ms）；320 px 重采样在窗口较大时明显更软。
- 用户明确要求“纯色模式”**不要透出底下内容**，并且**关掉**为在 mpv 上叠 XAML 毛玻璃而引入的
  抓帧/模糊渲染——性能优先。继续保留三档模糊模式无法满足该要求。
- 管线还带来 Win2D 依赖、OSD 注入的 XAML hack、以及控件隐藏后仍保留最后一帧的“脏快照”。

## 决策

- **删除** `src/Wiina.App/Interop/ChromeBlur.cs` 与 `src/Wiina.Core/Settings/ChromeBlurMode.cs`，
  移除 Win2D 包引用与 `ui.blur-mode` 设置行（`settings.json` 里遗留的 `"ChromeBlurMode"` 键在反序列化时被忽略）。
- 播放器外框（标题栏、顶部/底部/浮动 OSC、侧边栏、OSD）统一使用**不透明**主题画刷
  `ChromeSolidBrush`：
  - 深色 `#FF15171B`，浅色 `#FFF3F3F3`；
  - 高对比度主题使用 `SystemColorWindowColor`。
- 由 `ChromeTint`（`src/Wiina.App/Services/ChromeTint.cs`）把**系统 accent 以 16% 权重**混入
  基色，并订阅 `UISettings.ColorValuesChanged`，在 UI 线程重算画刷；切换 accent 立即生效。
- 外框不再采样视频，因此**没有透出、没有逐帧回读、没有 Win2D**；界面层级就是
  “视频 swapchain → XAML 不透明外框”。

## 后果

- 正面：外框渲染退化为一次纯色填充，CPU/GPU 回读与 Win2D 依赖整体消失；窗口尺寸、DPI、
  主题切换都不再影响外框成本；不再有 125 ms 滞后或“软掉”的模糊。
- 正面：OSD 不再需要注入外层 Grid 来做模糊裁切，XAML 树更简单，自动化树也不再包含这些注入层。
- 负面：失去“真视频背景毛玻璃”的观感；外框永远不透明。这是维护者在问题 2 中明确选择的取舍。
- 次要窗口（设置、欢迎、键位编辑）仍使用 `DesktopAcrylicBackdrop`，与本决策无关。
- 用户无法再切回模糊模式；文档与设置中不再出现“玻璃性能模式”。

## 备选方案

- **保留模糊但默认关闭**：仍需打包 Win2D、保留取帧管线与 OSD 注入代码，且“纯色模式”下
  用户仍会看到历史快照透出，不满足问题 2。
- **只调低模糊刷新率**：降低但不消除回读成本，滞后更严重，未解决“透出内容”。
- **Mica/Acrylic + 半透明覆盖**：SwapChainPanel 像素不参与 WinUI 合成，结果仍是平染色
  （ADR-0003 已记录），且会让视频内容透出，与“纯色”矛盾。

## 迁移说明

- 删除的设置键：`ChromeBlurMode`（对应设置行 id `ui.blur-mode`；旧 JSON 键被忽略，无需手动清理）。
- 删除的文件：`Interop/ChromeBlur.cs`、`Settings/ChromeBlurMode.cs`；对应的验收记录保留为历史。
- 新增/保留的主题键：`ChromeSolidBrush`（Dark/Light/HighContrast 三套 ThemeDictionaries）。
- 若将来要恢复某种“背景材质”，应从真实的合成层能力出发（例如视频输出自身的特效），
  而不是回到逐帧回读。
