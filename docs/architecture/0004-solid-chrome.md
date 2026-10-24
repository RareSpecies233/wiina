# ADR-0004：纯色模式（关闭毛玻璃）

- 状态：**Accepted**（2026-10-24；v1.2.4 修订：毛玻璃已回归为可选功能，本 ADR 只定义四档中的
  “关闭毛玻璃（纯色）”）
- 相关：[ADR-0003](0003-acrylic.md)（取帧 + Win2D 高斯模糊，v1.2.4 起的当前主设计）、
  `ChromeBlurMode.Solid`、`ChromeSolidBrush`、`src/Wiina.App/Services/ChromeTint.cs`

## 背景

1.2.3 曾经把取帧模糊整体删除，只保留不透明纯色外框。1.2.4 按维护者要求把毛玻璃恢复为**可选**的
四档（固定 / 动态 / 始终 / 关闭毛玻璃），同时保留一个**零开销**的纯色档，用于低性能设备、A/B
对比，以及“不希望外框透出任何画面”的场景。

取帧模糊的代价是真实的：每 125 ms 一次 `screenshot-raw` 全帧回读，1280×720 单帧约 45 ms、
3.7 MB，720p 下约占三分之一个 CPU 核心；4K 更高。纯色档存在的意义就是把这部分开销降为零。

## 决策

- `ChromeBlurMode.Solid`（枚举值 3）下：
  - **不启动取帧线程**，**不创建 Win2D `CanvasDevice`**，也丢弃任何在途帧；
  - 每个外框表面只画不透明主题画刷 `ChromeSolidBrush`：
    深色 `#FF15171B`、浅色 `#FFF3F3F3`、高对比度 `SystemColorWindowColor`；
  - `ChromeTint` 把系统 accent 以 **16% 权重**混入基色，并订阅
    `UISettings.ColorValuesChanged`，在 UI 线程重算画刷——切换 accent 立即生效。
- 纯色档**不透出任何视频内容**：视觉层级就是“视频 swapchain → XAML 不透明外框”。
- 回退路径同样使用纯色：`WIINA_BLUR_DISABLE=1` 让 `ChromeBlur.TryAttach` 直接失败；
  取帧/设备/发布失败时表面保持纯色画刷。切换档位是实时的（`ChromeBlur.ModeChanged`）。
- 1.2.3 写下的 `settings.json` 若含 `"ChromeBlurMode": "Solid"`，1.2.4 仍然按纯色加载；
  枚举值与键名都没有变。

## 后果

- 正面：纯色档没有任何回读、线程或 Win2D 依赖；窗口尺寸、DPI、主题切换都不影响外框成本。
  它也是模糊不可用（驱动/DRM/旧系统）时的稳定落点。
- 正面：低端设备有了明确、可解释的性能档位，而不是“模糊关不掉”。
- 负面：纯色档没有“真视频背景毛玻璃”的观感，这是该档位的定义，不是缺陷。
- 次要窗口（设置、欢迎、键位编辑）仍使用 `DesktopAcrylicBackdrop`，与本决策无关。
- OSD 消息文本在所有档位下都保持不透明；Docked 布局下标题栏也保持不透明（视频在栏下方）。

## 备选方案

- **整体删除模糊（1.2.3 的做法）**：把“纯色”变成唯一选项。1.2.4 已推翻——维护者要求模糊回归，
  同时保留纯色档。
- **保留模糊并把默认设为纯色**：仍能兼顾，但会让“默认动态”的既有用户升级后看到变化；
  当前默认仍是动态。
- **半透明覆盖 + 系统 Mica/Acrylic**：`SwapChainPanel` 像素不参与 WinUI 合成（见 ADR-0003），
  结果只会是平染色并透出内容，与纯色档语义矛盾。

## 迁移说明

- 设置键 `ChromeBlurMode` 继续有效，取值 `Fixed`/`Dynamic`/`Always`/`Solid`；未知值回退为
  `Dynamic`。设置行 id 仍是 `ui.blur-mode`。
- 1.2.3 期间删除的 `Interop/ChromeBlur.cs` 与 `Settings/ChromeBlurMode.cs` 已在 1.2.4 恢复；
  1.2.3 的验收记录保留为历史，其中的“模糊已移除”结论不再代表当前版本。
- 纯色档的画刷键仍是 `ChromeSolidBrush`（Dark/Light/HighContrast 三套 ThemeDictionaries）。
