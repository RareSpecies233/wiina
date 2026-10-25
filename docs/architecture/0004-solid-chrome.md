# ADR-0004：纯色模式（关闭毛玻璃）

- 状态：**Accepted**（2026-10-24；v1.2.5 修订：本 ADR 只定义四档中的「关闭毛玻璃（纯色）」，
  毛玻璃本身由 [ADR-0003](0003-acrylic.md) 描述）
- 相关：`ChromeBlurMode.Solid`（枚举值 3）、`ChromeSolidBrush`、
  `src/Wiina.App/Services/ChromeTint.cs`、`Themes/Tokens.xaml`

## 背景

外框可以按全局「毛玻璃模式」选择固定 / 动态 / 始终 / 关闭四档，默认动态。前三档要从 libmpv
回读视频帧（`screenshot-raw`，约 8 fps，320 px 降采样后 Win2D 高斯模糊），代价是真实的：
1280×720 单帧约 45 ms、3.7 MB，720p 下约占三分之一个 CPU 核心，4K 更高。纯色档存在的意义
是把这部分开销降为零，同时给低端设备、A/B 对比和「不希望外框透出任何画面」的场景一个明确
选项。

## 决策

`ChromeBlurMode.Solid`（枚举值 3）下：

- **不启动取帧线程**，**不创建 Win2D `CanvasDevice`**，并丢弃任何在途帧；
- 外框表面只画不透明主题画刷 `ChromeSolidBrush`：
  深色 `#FF202227`（1.2.5 起的深灰基色，不再是近黑）、浅色 `#FFF3F3F3`、
  高对比度 `SystemColorWindowColor`；
- `ChromeTint` 把系统 accent 以 **16% 权重**混入基色，并订阅
  `UISettings.ColorValuesChanged`，在 UI 线程重算画刷——切换 accent 立即生效；
- 纯色档**不透出任何视频内容**：1.2.5 起外框面板不再由代码写 `Background`，模糊层与 tint
  层是子元素、只切换 `Visibility`。因此纯色档在结构上不可能变透明，离开纯色档也不需要
  隐藏/显示外框就能恢复玻璃；
- 回退路径同样使用纯色：`WIINA_BLUR_DISABLE=1` 让 `ChromeBlur.TryAttach` 直接失败；
  取帧/设备/发布失败时表面保持纯色画刷。切换档位是实时的（`ChromeBlur.ModeChanged`）；
- OSD 卡片与全局档位一致：纯色档不透明，前三档为玻璃；它始终不接收输入。
- 1.2.3/1.2.4 写下的 `settings.json` 若含 `"ChromeBlurMode": "Solid"`，1.2.5 仍按纯色加载；
  枚举值与键名都没有变。

## 后果

- 正面：纯色档没有任何回读、线程或 Win2D 依赖；窗口尺寸、DPI、主题切换都不影响外框成本。
  它也是模糊不可用（驱动/DRM/旧系统）时的稳定落点。
- 正面：低端设备有了明确、可解释的性能档位，而不是「模糊关不掉」。
- 负面：纯色档没有「真视频背景毛玻璃」的观感，这是该档位的定义，不是缺陷。
- 次要窗口（设置、欢迎、键位编辑）仍使用 `DesktopAcrylicBackdrop`，与本决策无关。
- OSD 在纯色档下不透明；Docked 布局下标题栏保持不透明（视频在栏下方，模糊没有意义）。

## 备选方案

- **整体删除模糊（1.2.3 的做法）**：把纯色变成唯一选项。1.2.4 已推翻——维护者要求模糊回归，
  同时保留纯色档。
- **保留模糊并把默认设为纯色**：仍能兼顾，但会让「默认动态」的既有用户升级后看到变化；
  当前默认仍是动态。
- **半透明覆盖 + 系统 Mica/Acrylic**：`SwapChainPanel` 像素不参与 WinUI 合成（见 ADR-0003），
  结果只会是平染色并透出内容，与纯色档语义矛盾。

## 迁移说明

- 设置键 `ChromeBlurMode` 继续有效，取值 `Fixed`/`Dynamic`/`Always`/`Solid`；未知值回退为
  `Dynamic`；设置行 id 仍是 `ui.blur-mode`（设置 → 界面 → 窗口与 OSC）。
- 1.2.3 的验收记录保留为历史，其中「模糊已移除」的结论只描述 1.2.3；1.2.4 起毛玻璃是可选
  功能，1.2.5 起 OSD 也接入同一管线。
- 纯色档的画刷键仍是 `ChromeSolidBrush`（Dark/Light/HighContrast 三套 ThemeDictionaries）。
