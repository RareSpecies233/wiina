# IINA macOS UI 逻辑复刻规格

> 目标：把 IINA 在 macOS 上的**界面逻辑**（而不是 AppKit 代码）整理成一份平台无关的规格，供其他平台（Windows / Linux / Android / iOS）的 agent 直接照着实现。
>
> 基线：`iina/iina`，`develop` 分支，HEAD `5c15f81568b1f61f85219302843018e3c43c8d3b`（2026-10-16），v1.5.0 之后。源码在本工作区 `repo/` 下。
>
> 读法建议：先读 §1–§2 建立骨架和设计令牌，再按你要做的界面挑章节；§19 是落地顺序与验收清单，§20 是常量速查和源码索引。
>
> 证据标注：正文中的 `iina/Xxx.swift:123` 均指 `repo/iina/Xxx.swift` 的第 123 行。凡是本规格从代码推导出的近似值（例如由内边距累加得到的 OSC 高度），都会写“约”并给出推导依据。

---

## 0. 如何使用本规格

### 0.1 三条原则

1. **行为优先于像素**：IINA 的窗口尺寸、动画时长、按钮大小是 macOS 调校值。复刻时优先保证“层级、顺序、状态迁移、触发条件、避让规则”一致，像素值可按平台密度换算（见 §18）。
2. **一个主窗口 + 叠加层**：IINA 没有标签页式主界面。播放、侧栏、OSD、进度预览全部叠在同一个视频窗口上；只有设置、欢迎、插件独立窗口是真正的二级窗口。
3. **状态机显式化**：OSC 显隐、OSD 显隐、侧栏显隐、全屏、迷你播放器、PiP 都有明确的 `shown/hidden/willShow/willHide` 或 `windowed/animating/fullscreen` 状态；不要用“如果可见就……”的散落判断替代状态机。

### 0.2 术语

| 术语 | 含义 |
|---|---|
| OSC | On Screen Controller，播放器底部/顶部/浮动的控制条 |
| OSD | On Screen Display，短暂提示（暂停、音量、跳转、错误等） |
| 浮动 OSC | 悬浮在视频上、可拖动的控制条，IINA 默认形态 |
| 停靠（docked） | 标题栏/控制条不覆盖视频，视频区域被夹在中间 |
| edge-to-edge | 视频铺满内容区，标题栏/OSC 浮在上面（IINA 默认） |
| 快速设置 | 右侧/左侧边栏里的四个标签页：布局、视频、音频、字幕 |
| 音乐模式 | 迷你播放器窗口，音频优先，可折叠播放列表 |

### 0.3 视觉基准

IINA 默认是**深色主题 + 浮动 OSC + edge-to-edge 视频 + 无侧栏**。任何“默认值”都以此为准；设置项只改变默认，不改变结构。

### 0.4 给实现 agent 的启动提示词（可直接复制）

```text
你正在为目标平台（填：Windows / Linux / Android / iOS）复刻 IINA 的播放器 UI。
请先完整阅读《IINA-macOS-UI-复刻规格.md》，然后：

1. 用目标平台的 UI 框架给出“窗口/视图层级”方案，对应规格 §1.2 和 §3；
2. 先实现静态骨架（§19.1 第 1 步），再实现 OSC（§5），最后接播放内核；
3. 所有尺寸以规格中的 pt 为基准，按平台密度换算；颜色用 §2.2 的 RGBA；
4. 不要照搬 AppKit API；用 §18 的映射表选择等价能力；
5. 每完成一个阶段，按 §19.2 的验收清单逐条自测；
6. 遇到规格未覆盖的细节，回到 IINA 源码（路径见 §20.2）确认，不要臆造。

输出：一份目标平台的实现计划 + 关键界面伪代码 + 验收结果。
```

---

## 1. 界面总览

### 1.1 窗口与模式矩阵

| 界面 | 类型 | 何时出现 | 关键状态 |
|---|---|---|---|
| 主窗口 | 普通窗口 | 打开文件后 | 窗口化 / 全屏；edge-to-edge / docked；有无侧栏 |
| 浮动 OSC | 主窗口子视图 | `oscPosition == .floating`（默认） | shown / hidden |
| 底部 OSC | 主窗口子视图 | `oscPosition == .bottom` | shown / hidden |
| 顶部 OSC | 标题栏内子视图 | `oscPosition == .top` | 随标题栏显隐 |
| 侧边栏（左/右） | 主窗口子视图 | 点击 OSC 工具栏或菜单 | hidden / settings / playlist / plugins |
| 进度时间预览 | 主窗口子视图 | 鼠标悬停进度条 | 跟随鼠标，瞬时 |
| 缩略图预览 | 主窗口子视图 | 鼠标悬停进度条且缩略图就绪 | 跟随鼠标，瞬时 |
| 设置窗口 | 独立窗口 | ⌘, 或菜单 | 11 个页面，可搜索 |
| 欢迎窗口 | 独立窗口 | 启动时（可配置） | 最近文件列表 |
| 迷你播放器 | 独立窗口 | 音乐模式 | 可展开播放列表 / 专辑封面 |
| 画中画 | 系统 PiP 窗口 | 工具栏/菜单/快捷键 | 进入 / 退出 |
| 插件窗口 | 独立窗口 | 插件调用 | 600×400 起 |
| 检查器 / 滤镜 / 按键编辑 / 关于 / 日志 / 打开 URL | 独立窗口 | 菜单 | 辅助功能 |

### 1.2 主窗口视图栈（从底到顶）

```
window.contentView
├─ videoViewContainer                    ← 视频层（最底）
│   └─ videoView (CAOpenGLLayer/CAMetalLayer)
├─ pluginOverlayViewContainer            ← 插件全屏覆盖层（默认隐藏）
├─ pipOverlayView                        ← 进入 PiP 后的占位/提示层
├─ leadingSidebar.view  /  trailingSidebar.view
├─ oscBottomView                         ← 底部 OSC（仅 bottom 模式）
├─ oscFloatingView                       ← 浮动 OSC（仅 floating 模式）
├─ titleBarView                          ← 自绘标题栏（含顶部 OSC）
├─ additionalInfoView                    ← 全屏时的时钟/电量（左下）
├─ osdView                               ← OSD（右下）
├─ timePreviewView / thumbnailPeekView   ← 进度预览（跟随鼠标）
└─ bufferIndicatorView                   ← 缓冲/跳转指示（居中）
```

证据：`iina/MainWindowController.swift:443-690`（`windowDidLoad` 的添加顺序）；`iina/MainWindowController.swift:425-427`（`mouseActionDisabledViews` 包含侧栏、当前控制条、标题栏）。

> 实现提示：视频层必须独立于 UI 层（不要用同一个合成层做透明叠加），否则全屏 HDR/10-bit 会被 UI 混合破坏。IINA 用 `CAOpenGLLayer` + `CVDisplayLink` 单独渲染视频（`iina/VideoView.swift:216`、`iina/ViewLayer.swift:67`）。

### 1.3 全局交互规则

- **鼠标进入窗口** → 显示 UI 并重置自动隐藏计时器；**鼠标离开窗口** → 立即隐藏 UI（`iina/MainWindowController.swift:1257-1295`）。
- **鼠标在 OSC/标题栏内** → 取消隐藏计时器；在视频区域移动 → 重新计时（`iina/MainWindowController.swift:1297-1310`）。
- **拖拽窗口**：在视频区域按住拖动超过 3pt 才开始拖窗口，否则算点击（`iina/MainWindowController.swift:33`、`:1115-1145`）。
- **单击/双击**：若配置了双击动作，单击要等系统双击间隔后再执行；双击会取消单击计时器（`iina/PlayerWindowController.swift:405-430`）。
- **光标隐藏**：UI 隐藏时调用“鼠标移动前隐藏光标”；macOS 上还要在按键时重复调用以绕过系统缺陷（`iina/MainWindowController.swift:1102`）。
- **RTL**：视频进度条、音量条、播放控制簇强制从左到右；侧栏、菜单、OSD 文本按系统方向（`iina/MainWindowController.swift:610`、`iina/PlayerWindowController.swift:210`）。

---

## 2. 设计令牌

### 2.1 主题与材质

| 令牌 | 值 | 说明 |
|---|---|---|
| 主题 | `dark` / `light` / `system` | 默认 `dark`；窗口背景深色为纯黑、浅色为纯白（`iina/PlayerWindowController.swift:262-266`） |
| 标题栏材质 | `.titlebar` | 未停靠时覆盖视频，带 1px 底边 |
| 侧栏材质 | `.sidebar` / 半透明 | 浮动侧栏有 0.3 黑阴影、6pt 模糊；停靠时改为 1px 分隔线（`iina/SideBar.swift:37-70`） |
| OSD/预览材质 | `.popover` / 半透明 | 视觉模糊 + 圆角 |
| 迷你播放器背景 | `.underWindowBackground` | 窗口整体毛玻璃 |
| 新材质（macOS 26） | Liquid Glass | 设置项 `useLiquidGlassOSC/OSD/Sidebar`，默认开；老系统回退 `NSVisualEffectView` |

> 跨平台替代：Windows 用 Mica/Acrylic，Linux 用 GTK 的模糊/半透明或合成器 blur，Android 用 `RenderEffect`/半透明 scrim，iOS 用 `UIVisualEffectView`。没有模糊能力时退化为 85% 不透明黑（深色）/白（浅色）。

### 2.2 颜色（sRGB，来自 `iina/Assets.xcassets/*.colorset`）

| 名称 | Light | Dark | 用途 |
|---|---|---|---|
| `TitleBarBorder` | 0.659 灰 α1 | 黑 α1 | 标题栏 1px 底边 |
| `MainSliderBarLeft` | 黑 α0.40 | 白 α0.30 | 进度条已播放段 |
| `MainSliderBarRight` | 黑 α0.20 | 白 α0.10 | 进度条未播放段 |
| `MainSliderKnob` | 白 α0.96 | 白 α0.80 | 进度条滑块 |
| `MainSliderKnobActive` | 白 α0.75 | 白 α0.98 | 拖动中的滑块 |
| `MainSliderLoopKnob` | 0.410 灰 α1 | 0.650 灰 α1 | A-B 循环点 |
| `VolumeSliderBarLeft` | 黑 α0.40 | 白 α0.30 | 音量已填充 |
| `VolumeSliderBarRight` | 黑 α0.10 | 白 α0.10 | 音量未填充 |
| `SidebarContainerBorder` | 黑 α0.15 | 白 α0.08 | 侧栏卡片边框 / 音量弹层边框 |
| `SidebarTabTint` | 黑 α0.50 | 白 α0.50 | 未选中标签图标 |
| `SidebarTabTintActive` | 黑 α1 | 白 α1 | 选中标签图标 |
| `SidebarTabBtnBorder` | 黑 α0.25 | 白 α0.25 | 选中标签胶囊描边 |
| `SidebarTableBackground` | 黑 α0.02 | 白 α0.08 | 播放列表底 |
| `PlaylistProgressBar` | 黑 α0.20 | 白 α0.60 | 播放列表行内进度 |
| `CropBoxFill` | 0.5 灰 α0.30 | 同 | 交互式裁剪遮罩 |
| `InitialWindowLastFileBackground` | 黑 α0.10 | 白 α0.10 | 欢迎页“继续播放”行 |
| `InitialWindowBetaLabel` | (1.0,0.55,0.2) | (1.0,0.65,0.25) | Beta 标签 |
| `InitialWindowNightlyLabel` | (0.584,0.302,1.0) | 同 | Nightly 标签 |

补充：OSC 文本用系统 `secondaryLabelColor`（约 60% 前景），主文本用 `labelColor`；滑杆的浅色主题滑块带 1pt 阴影（`iina/PlaySliderCell.swift:49-77`）。

### 2.3 字体

| 位置 | 字号/字重 | 备注 |
|---|---|---|
| 标题栏标题 | 13pt，系统字体 | 中间截断，`iina/Titlebar.swift:168` |
| OSC 时间 | 11pt 等宽数字 | `monospacedDigitSystemFont`，最小宽 46 |
| OSC 速度标签 | 10pt | 1x 时隐藏 |
| 侧栏标签 | 11pt | 图标在上、文字在下 |
| 侧栏卡片标题 | 12pt 粗体 | 轨道选择器等 |
| 播放列表行 | 系统 12–13pt | 两行：标题 + 元数据 |
| OSD 主文本 | 20pt（可设置 1–500） | 等宽数字，`osdTextSize` |
| OSD 次要文本 | 主字号 ×0.5，clamp 11–25 | 章节名等 |
| OSD 图标 | 主字号 ×0.8 | SF Symbol |
| 迷你播放器标题 | 13pt 粗体 | 居中，滚动显示 |
| 迷你播放器艺术家 | 11pt | 次要色 |
| 设置页标签 | 系统 13pt；说明 11pt | 值控件右对齐 |
| 欢迎页标题 | 20pt 粗体 | IINA |
| 全屏时间/电量 | 18pt；时间粗体等宽 | `AdditionalInfoView` |

### 2.4 尺寸 / 间距 / 圆角

| 令牌 | 常规 | 紧凑（Compact UI） | 说明 |
|---|---|---|---|
| 主窗口最小尺寸 | 285×120 | 同 | `AppData.mainWindowMinSize` |
| 无视频窗口 | 640×360 | 同 | 打开音频时的默认宽高比 |
| 标题栏高度 | 40 / 36（macOS 26 / 旧） | 32 / 28 | `Titlebar.swift:9-18`，常规 = 基准 + 8 |
| 标题栏左右内边距 | 12 | 8 | `padding(.horizontal)` |
| 标题栏文档图标区 | 起点 86pt | 同 | 避开红绿灯 |
| 标题文字与图标间距 | 2pt | 同 | 图标 16×16 |
| 标题栏按钮 | 16pt 高，间距 8 | 同 | pin / 去黑边 |
| 浮动 OSC 宽 | 460（可压缩到 200） | 同 | `OSCFloatingView.swift:12` |
| 浮动 OSC 高 | 约 67 | 同 | 由两行内容 + padding 推导 |
| 浮动 OSC 圆角 | 12 | 6 | macOS 26 / 旧 |
| 底部 OSC 左右内边距 | 16 左 / 12 右 | 12 / 8 | `OSCBottomView.swift:8-10` |
| 底部 OSC 上下内边距 | 12 | 8 | 同上 |
| 顶部 OSC 高 | 24 | 同 | 标题栏内 |
| OSC 按钮 | 24×24 | 20 宽（浮动 5 键时） | `Preference.ToolBarButton.frameSize` |
| 播放键间距 | 16（停靠）/ 24（浮动） | 同 | `MainWindowController.swift:1044` |
| 侧栏宽 | 默认 360，范围 300–500 | 同 | 另受窗口宽 80% 限制 |
| 侧栏外边距 | 16 | 14 | `LayoutValue.sidebarMargin` |
| 侧栏卡片间距 | 18 | 16 | `sidebarStackViewSpacing` |
| 侧栏卡片内边距 | 12 | 10 | `sidebarContainerPadding` |
| 侧栏卡片圆角 | 8 | 8 | NSBox cornerRadius 8 |
| 侧栏标签栏高 | 52 | 48 | `SideBar.swift:301` |
| 侧栏关闭按钮 | 36 | 28 | `closeSidebarBtnSizeConstraint` |
| 侧栏缩放热区 | 4pt | 同 | 内侧边缘 |
| OSD 圆角 | 16 / 10 | 同 | Liquid / VEV |
| OSD 内边距 | 16 横 / 8 纵 | 同 | |
| OSD 进度条高 | 12 | 同 | |
| 时间预览圆角 | 16 / 8 | 同 | |
| 时间预览内边距 | 4 | 同 | |
| 缩略图圆角 | 4 | 同 | 1px 边框 0.6 灰 α0.5 |
| 迷你播放器高 | 72 + 视频高 | 同 | 播放列表再 +300 |
| 迷你播放器按钮 | 28；次要 18 | 同 | 关闭 12/13 |
| 设置窗口 | 600×480 | 同 | 侧栏固定 200，内容最小 400 |
| 欢迎窗口 | 560×440 | 同 | 内容边距 30 |
| 欢迎页图标 | 68 | 同 | |
| 欢迎页最近文件行高 | 28 | 同 | |
| 插件窗口 | 600×400 | 同 | 可缩放 |

### 2.5 动画与时间

| 事件 | 时长/曲线 | 说明 |
|---|---|---|
| OSC/标题栏显隐 | 0.25s 淡入淡出 | `UIAnimationDuration` |
| OSD 隐藏 | 0.5s 淡出 | `OSDAnimationDuration` |
| OSD 持久附件展开 | 0.3s | 窗口同时放大 |
| 侧栏滑入/滑出 | 0.24s `easeIn` | `SideBarAnimationDuration` |
| 侧栏标签切换 | 0.3s push 左/右 | 系统“减弱动态”或禁用动画时无动画 |
| 迷你播放器控件显隐 | 0.2s | 标题信息反向淡出 |
| 迷你播放器音量弹层 | 0.2s | 宽度从 10 → 148 |
| 设置侧栏分区指示器 | 0.1s | 小圆点随滚动移动 |
| 设置搜索高亮 | 1s | 4px 白 + 3px 强调色描边，80% 处开始淡出 |
| 设置页跳转滚动 | 0.25s | 目标位于视口 33% 处 |
| 全屏进出 | 系统时长（约 0.3–0.5s） | 自定义动画把窗口推到屏幕 frame |
| 双击判定 | 系统双击间隔 | `NSEvent.doubleClickInterval` |
| OSC 自动隐藏 | 2.5s | `controlBarAutoHideTimeout`，可设置 |
| OSD 自动隐藏 | 1s | `osdAutoHideTimeout`，可设置 |
| 提示音/触觉 | 吸附对齐、音量过 100% | macOS 触觉反馈 |

**减弱动态 / 禁用动画**：所有动画时长乘以 `AccessibilityPreferences.adjustedDuration`；若用户开启“禁用动画”，侧栏改为淡入（0 时长滑动会闪烁），OSD 直接切换，窗口缩放不 animate（`iina/AccessibilityPreferences.swift:16`、`iina/SidebarController.swift:260-266`）。

---

## 3. 主窗口

### 3.1 窗口属性

| 属性 | 值 |
|---|---|
| 样式 | `titled + closable + miniaturizable + resizable + fullSizeContentView` |
| 标题栏 | 透明；自绘标题栏覆盖内容区顶部 |
| 背景 | 深色纯黑 / 浅色纯白 |
| 最小尺寸 | 285×120 |
| 宽高比 | 默认锁定为视频宽高比；`unlockWindowAspectRatio` 时解除 |
| 初始尺寸 | 无视频 640×360；有视频按视频尺寸并居中/适配屏幕 |
| 窗口缩放 | 同步 mpv `window-scale`；1x 可按物理分辨率或逻辑分辨率 |

### 3.2 两种布局：edge-to-edge vs docked

```
edge-to-edge（默认：edgeToEdgeVideo = true）
┌──────────────────────────────┐
│ 视频铺满整个 contentView      │
│  [标题栏浮在顶部]             │
│                               │
│  [浮动 OSC 浮在视频上]        │
└──────────────────────────────┘

docked（edgeToEdgeVideo = false 且 dockedControlBarAndTitlebar = true）
┌──────────────────────────────┐
│ 标题栏（占位，不覆盖视频）     │
├──────────────────────────────┤
│ 视频区域（被侧栏夹住）         │
├──────────────────────────────┤
│ 底部 OSC（占位）              │
└──────────────────────────────┘
```

- 判断式：`Preference.isDocked = !edgeToEdgeVideo && dockedControlBarAndTitlebar`（`iina/Preference.swift:1011-1013`）。
- edge-to-edge 时视频容器四边贴 contentView；docked 时上贴标题栏底、下贴底部 OSC 顶、左右贴侧栏（`iina/MainWindowController.swift:870-900`）。
- 侧栏显隐会实时改变视频容器左右约束；标题栏/底部 OSC 的左右约束同步跟随侧栏内边缘（`iina/SidebarController.swift:266-280`）。
- 窗口首次按视频尺寸调整时，如果是 docked，要把标题栏和底部 OSC 的高度补进窗口高度（`iina/MainWindowController.swift:2570-2578`）。

### 3.3 视频区域

- 视频视图用 `videoViewContainer` 包一层，真正视频层四边贴满容器；需要黑边/裁剪时通过修改 `videoViewConstraints` 的常量实现（`iina/MainWindowController.swift:800-860`）。
- `unlockWindowAspectRatio` 打开时，标题栏出现“去除黑边”按钮；点击后按视频宽高比裁剪窗口并居中（`iina/MainWindowController.swift:860-880`、`iina/Titlebar.swift:264-268`）。
- 窗口 resize 期间视频层进入 `inLiveResize`，暂停时禁止重复强制绘制；resize 结束同步 mpv `window-scale`。

### 3.4 窗口缩放与全屏状态

窗口尺寸菜单项：Half（0.5x）、Normal（1x）、Normal Retina（1x 物理）、Double（2x）、Fit to Screen、Bigger、Smaller（`iina/MenuController.swift:289`）。

全屏状态机（`iina/MainWindowController.swift:178-240`）：

```
windowed ──toggle──> animating(toFullscreen: true) ──完成──> fullscreen
   ^                                                        │
   └──── animating(toFullscreen: false) <──toggle───────────┘
```

- 进入全屏：显示标题栏（若 OSC 在顶部则标题栏内容切换为 OSC）、隐藏红绿灯与标题（动画结束后恢复）、关闭置顶、隐藏预览、退出交互模式。
- 退出全屏：把窗口 animate 回 `priorWindowedFrame`；进入前先隐藏菜单栏，结束后恢复。
- 失败回滚：`windowDidFailToEnterFullScreen` 必须恢复窗口化状态、标题栏、fadeableViews、置顶状态与绘制。

---

## 4. 标题栏

### 4.1 结构（自绘，非系统 titlebar）

```
┌────────────────────────────────────────────────────────────────────┐
│ (红绿灯区 86pt) [doc icon 16] 2 [标题 13pt 中间截断]      [去黑边] 8 [pin] 8 │
│                         （顶部 OSC 时，这一行换成 OSC 内容）          │
└────────────────────────────────────────────────────────────────────┘
  1px 底边（TitleBarBorder）
```

- 高度：常规 40（macOS 26）/ 36；紧凑 32 / 28。
- 背景：`NSVisualEffectView(material: .titlebar)`，上下各留 1px；底边 1px。
- 容器：垂直 `NSStackView`，上边贴顶，右边贴窗口；标题行与 OSC 行二选一显示。
- 文档图标：16×16，距左 ≥86；标题距图标 2pt，距右侧按钮组 ≥8。
- 右侧按钮组：`[去黑边][8][pin]`，距右 8，垂直居中；按钮高 16。
- 标题字体 13pt，`byTruncatingMiddle`；标题文本有 200 的抗压缩优先级。

### 4.2 三种形态

| 形态 | 条件 | 标题 | OSC 行 |
|---|---|---|---|
| 窗口化 | 非全屏 | 显示 | 仅 `oscPosition == .top` 时显示 |
| 全屏 + OSC 顶部 | 全屏 | 隐藏（`notVisible`） | 显示，顶部额外 8pt 内边距 |
| 全屏 + OSC 非顶部 | 全屏 | 显示系统标题栏 | 隐藏 |

- 切换逻辑在 `Titlebar.update(hasOSC:inFullScreen:)`（`iina/Titlebar.swift:235-256`）。
- 全屏时 `window.titleVisibility = .visible` 显示系统标题；窗口化时隐藏，使用自绘标题。
- 顶部 OSC 的垂直约束：窗口化时标题栏底贴窗口顶；全屏且有 OSC 时标题栏顶贴窗口顶，否则整体移出屏幕（`updateVerticalConstraint`）。

### 4.3 标题与文档图标

- 网络资源：标题用媒体标题（`player.getMediaTitle()`），无文档图标。
- 本地文件：标题用文件名（`FileManager.displayName`），文档图标用 `NSWorkspace.icon(forFile:)`。
- 标题栏标题、系统窗口标题、Dock/Window 菜单标题三者保持一致；窗口复用时先把标题重置为 "Window" 以绕过 AppKit 缓存缺陷（`iina/MainWindowController.swift:775-800`）。

### 4.4 按钮

| 按钮 | 图标 | 显示条件 | 行为 |
|---|---|---|---|
| pin | `pin` / `pin.fill` | `alwaysShowOnTopIcon` 或当前置顶 | 切换窗口置顶 |
| 去黑边 | `remove.blackbars` | `unlockWindowAspectRatio` | 按视频宽高比裁剪窗口并居中 |

- pin 按钮 16pt 高，`smallSquare`、无边框；填充态表示置顶。
- 去黑边按钮同样 16pt 高；不满足条件时隐藏（不是禁用）。

### 4.5 路径菜单

- 右键标题/文档图标，或 ⌘+左键文档图标 → 弹出路径面包屑。
- 菜单项从文件所在目录一路到根目录，每项带文件系统图标；选择某一级目录会在 Finder 中显示其下一级（`iina/Titlebar.swift:300-350`）。
- 菜单出现在标题文字位置（`NSPoint(x: -30, y: 0)`，相对标题字段）。

### 4.6 全屏细节

- 进入全屏前把红绿灯 `alpha = 0`、标题 `alpha = 0`；动画结束后恢复为 1 并取消隐藏。
- 窗口化时 `titlebarAppearsTransparent = true`，用自绘标题栏；进入全屏后设为 `false`，让系统标题栏材质接管。
- 全屏下鼠标移出窗口仍会隐藏 UI；但标题栏只在 OSC 顶部模式下随 UI 显隐。

---

## 5. OSC（On Screen Controller）

### 5.1 三种位置

| 位置 | 宿主 | 结构 | 默认可见性策略 |
|---|---|---|---|
| `floating`（默认） | `oscFloatingView`（主窗口子视图） | 两行：上排控制/音量/工具，下排进度条 | `auto`（鼠标移动显示，2.5s 后隐藏） |
| `bottom` | `oscBottomView`（贴底全宽） | 一行：控制簇 + 进度条 + 音量 + 工具 | `auto` |
| `top` | `titleBarView.oscView` | 一行，塞进标题栏 | 随标题栏 `auto` |

- 切换位置时先拆掉所有片段视图，再按新位置重装（`setupOnScreenController(withPosition:forced:)`，`iina/MainWindowController.swift:960-1050`）。
- 速度标签只在浮动 OSC 显示；顶部/底部隐藏。
- 片段可见性优先级：
  - 浮动：音量/工具 `detachOnlyIfNecessary`，中间控制簇居中。
  - 顶部/底部：进度条 `mustHold`，音量 `detachEarly`，工具栏 `detachEarlier`。
- 窗口过窄时优先牺牲工具按钮，其次音量，最后才压缩进度条。

### 5.2 通用片段

#### 播放控制簇

```
[左箭头 24] 16/24 [播放/暂停 24] 16/24 [右箭头 24]
```

- 三个按钮都是 `smallSquare`、无边框、24×24。
- 浮动模式下左右各带速度标签容器（30pt 宽，文字 10pt，1x 时隐藏）；停靠/顶部模式标签不占位。
- 播放按钮图标：暂停状态显示 `play`，播放状态显示 `pause`。
- 左右箭头行为由设置 `arrowButtonAction` 决定：
  - `speed`：按住加速/减速，支持 Force Touch 多级加速度；单击左 = 0.5x 一档，单击右 = 2x 一档；长按/重按按压力等级加速；松开回到 1x（`iina/MainWindowController.swift:2836-2935`）。
  - `seek`：左 -10s，右 +10s。
  - `playlist`：上一首/下一首。

#### 进度条 + 时间

```
[当前时间 46+] 2 [========== 进度条 ==========] 2 [总时长/剩余 46+]
```

- 时间标签 11pt 等宽数字、次要色、居中、最小宽 46。
- 右侧标签可切换“总时长 / 剩余时间”（设置 `showRemainingTime`）。
- 进度条自绘（`iina/PlaySliderCell.swift`）：
  - 滑轨高约 3–4pt，圆角 1.5。
  - 滑块 3×15，圆角 1；浅色主题加 1pt 阴影。
  - 已播放段/未播放段用 §2.2 的两档灰；浅色主题在滑块两侧留 1px 间隙。
  - 章节模式：每个章节起点画 1px 竖线（跳过第 1 章），随进度条裁剪（`showChapterPos`）。
  - 网络资源：已缓冲区域用 `cacheTime/duration` 画到进度条上，至少比滑块多 2px。
  - A-B 循环：两个额外旋钮（`PlaySliderLoopKnob`），隐藏当 A/B 为 0；拖动旋钮实时写 mpv 并弹 OSD。
  - 拖动进度条：按下时暂停（记录原状态），松开后若原本在播放则恢复；拖动时隐藏缩略图。
  - 禁用滚轮改进度：`disablePlaySliderScrolling`。

#### 音量

```
[静音按钮 24] 4 [音量条 70] 6
```

- 静音按钮是自绘 `VolumeButton`，图标阈值：
  - 静音/0：`speaker.slash.fill` / `speaker.fill`
  - 1–33：`speaker.wave.1.fill`
  - 34–66：`speaker.wave.2.fill`
  - ≥67：`speaker.wave.3.fill`
- 音量条 70pt，`controlSize = .mini`；最大值 = 设置 `maxVolume`（可 >100）。
- 超过 100% 时，在 100% 位置画 2px 缺口，提示“已超过标称音量”。
- 拖动到 >100 且 <101 时触发一次触觉反馈。
- 音量条可禁用滚轮（`disableVolumeSliderScrolling`）。

#### 工具栏

- 每个按钮 24×24。代码预留了 20pt 的压缩宽度参数（`compactFrameWidth`），当前调用始终为 24；若你的浮动 OSC 要放 5 个以上按钮，可按 20pt 压缩。
- 图标用 SF Symbols，点大小 14（pip/plugins 13.5），`medium` 字重。
- 默认按钮顺序：`[plugins, pip, playlist, settings]`（`Preference.defaultPreference`）。
- 可选按钮及图标：

| 按钮 | 图标 | 选中/替代图标 | 行为 |
|---|---|---|---|
| settings | `gearshape` | `gearshape.fill` | 打开快速设置侧栏 |
| playlist | `list.bullet.rectangle` | `list.bullet.rectangle.fill` | 打开播放列表侧栏 |
| pip | `pip.enter` | `pip.exit` | 进入/退出画中画 |
| fullScreen | `arrow.up.backward.and.arrow.down.forward.rectangle` | 反向箭头 | 全屏 |
| musicMode | `microphone.dynamic.on.stand` | — | 音乐模式 |
| subTrack | `captions.bubble.fill` | — | 弹出字幕选择菜单 |
| screenshot | `camera.shutter.button` | — | 截图 |
| plugins | `puzzlepiece.extension` | `.fill` | 插件侧栏 |
| liveText | `document.viewfinder` | `viewfinder.circle.fill` | 实时文字开关 |

- 悬停显示 tooltip：`osc_toolbar.<key>` 本地化文案。
- 侧栏状态变化、PiP 状态、全屏状态会切换对应按钮的填充图标（`iina/MainWindowController.swift:937-958`）。

### 5.3 浮动 OSC 布局

```
┌──────────────────────────────────────────────┐  ← 圆角 12（旧 6）
│ 12   [音量 24+70]   [← 24] 24 [▶ 24] 24 [→ 24]   [工具…]   12 │  ← 上内边距 14/10
│ 8    [当前] 2 [============ 进度 ============] 2 [总长]    8 │  ← 下内边距 8/5
└──────────────────────────────────────────────┘
```

- 宽 460（抗压缩优先级 300），最小 200；窄于 460 时整体压缩。
- 上排：音量组靠左、控制簇居中、工具栏靠右；三组都用 `detachOnlyIfNecessary`，窗口太窄时依次隐藏。
- 下排：进度条 + 左右时间，整行贴边（只留 8 的左右内边距）。
- 位置：相对视频容器，默认 `(0.5, 0.1)`（水平居中、垂直靠下 10%），可拖动保存。
- 位置计算会夹在视频容器内：左/右至少留 0，底部留 25pt 给“上浮”空间；高度按 67 估算（`OSCFloatingView.swift:100-135`）。

### 5.4 底部 OSC 布局

- 全宽，贴窗口底；内容左右内边距 16/12、上下 12/8。
- 一行：左控制簇、进度条（可伸缩）、右侧音量 + 工具栏。
- 顶部圆角：liquid 0（贴边），旧材质 0；背景是 `TranslucentView`（全宽模糊条）。
- 高度由内容决定，约 67；不参与窗口布局（覆盖在视频上），除非 docked（docked 时视频底部让位）。
- 侧栏显示时，左右约束分别增加侧栏宽度，避免控制条被侧栏压住（`SidebarController.setTitlebarConstraint` 同步 `oscBottomView`）。

### 5.5 顶部 OSC 布局

- 塞进标题栏第二行，高 24，左右内边距 6，底部留 10/6。
- 从左到右：控制簇、进度条、音量、工具栏；进度条 `mustHold`、音量 `detachEarly`、工具栏 `detachEarlier`。
- 全屏时标题行隐藏，OSC 行获得顶部 8pt 额外空间；窗口化时标题行与 OSC 行同时存在，标题栏高度要容纳两行（40/36 + 24）。

### 5.6 拖动与吸附

- 按住浮动 OSC 任意空白处拖动；拖动期间主窗口不移动。
- 水平接近窗口中心 ±5pt 时吸附到中心，并触发一次对齐触觉（`controlBarStickToCenter`）。
- 边界：左 ≥10，右 ≥10，顶 ≥0，底留 25。
- 松开后把位置保存为视频容器宽高的比例（0–1），下次打开恢复。
- RTL 下水平坐标镜像。

### 5.7 显隐状态机

```
shown ──mouse move/enter──> willShow ──0.25s──> shown
shown ──timer 2.5s / mouse exit──> willHide ──0.25s──> hidden
willShow/willHide ──反向事件──> 取消，回到 shown/hidden
```

- 触发显示：鼠标进入窗口、鼠标在窗口内移动、播放状态变化后刷新 UI。
- 触发隐藏：计时器到期、鼠标离开窗口、单击动作 = `hideOSC`、全屏下鼠标静止。
- 不隐藏的情况：浮动 OSC 正在拖动、PiP 中、交互模式（裁剪/去黑边）、实时文字激活、用户关闭自动隐藏、鼠标在 OSC/标题栏内。
- 隐藏时把所有 `fadeableViews` 的 alpha 动画到 0，然后 `isHidden = true`；窗口红绿灯用 `alpha = 1e-100` 而不是隐藏，避免系统按钮布局跳动（`iina/MainWindowController.swift:2082-2145`）。
- 显示时先把 `isHidden = false`，再 alpha 到 1；标题文字单独淡入/淡出（窗口化时）。
- 动画打断：用 `animationState` 判断，只有仍是 `willHide` 才在回调里真正隐藏；`willShow` 同理。

---

## 6. 播放控制与输入

### 6.1 鼠标

| 手势 | 默认行为 | 可配置 |
|---|---|---|
| 左键单击视频 | 隐藏 OSC（`hideOSC`） | 无 / 暂停 / 全屏 / 隐藏 OSC / PiP / A-B 循环 / 重置速度 |
| 左键双击视频 | 全屏（`fullscreen`） | 同上 |
| 右键单击 | 暂停（`pause`） | 同上 |
| 中键单击 | 无（`none`） | 同上 |
| 单击标题栏两次 | 系统“最小化/最大化”设置 | 跟随 `AppleActionOnDoubleClick` |
| 拖动视频 | 拖动窗口（>3pt 才成立） | — |
| Force Touch 第二段 | 由设置决定 | 全屏 / 隐藏 OSC / PiP / A-B / 重置速度 |

- 单击/双击消歧：若双击动作不是 `none`，单击延迟 `doubleClickInterval` 执行；双击取消单击计时器。
- 单击动作是 `hideOSC` 时，如果延迟期间鼠标又进出窗口（计数 ≥2），取消隐藏，避免“点一下想暂停却把 UI 藏了”（`iina/PlayerWindowController.swift:565-578`）。

### 6.2 滚轮 / 触控板

- 方向判定：一次手势开始时看 `scrollingDeltaX != 0` → 水平，否则垂直；触控板结束清除方向。
- 动作映射：水平/垂直各自可设为 音量 / 跳转 / 无 / 播放速度。
- 跳转量：
  - 鼠标：`[0, 0.5, 1, 2, 4]` 秒 × delta（设置 `relativeSeekAmount` 选档）。
  - 触控板：`[0, 0.05, 0.1, 0.25, 0.5]` 秒 × delta。
- 音量：`[0, 0.25, 0.5, 0.75, 1]` × delta；鼠标直接用 delta。
- 速度：`[0, 0.001, 0.002, 0.005, 0.01]` × 当前速度 × delta，clamp 0.05–4.0，四舍五入到 0.001。
- 跳转开始时若正在播放先暂停，手势结束后恢复（只恢复“原本在播”的情况）。
- 自然滚动方向：反向时取反 delta；鼠标滚轮 delta 乘 2。
- 鼠标在进度条/音量条上滚动时强制对应动作（`seekOverride` / `volumeOverride`），不受全局方向设置影响。
- 迷你播放器：音量滚动时弹出音量气泡；触控板开始显示、结束隐藏；鼠标在停止滚动 1s 后隐藏。

### 6.3 捏合

- `pinchAction`：
  - `windowSize`：按捏合比例缩放窗口，保持宽高比，限制在屏幕可见区域和最小尺寸之间；结束时同步 mpv。
  - `fullscreen`：放大进入全屏、缩小退出全屏（只在手势开始时判断一次）。
  - `none`：忽略。

### 6.4 键盘

- 按键先归一化为 mpv 键名（如 `Meta` = ⌘，`Alt` = ⌥），查 `PlayerCore.keyBindings`；命中则执行 mpv 命令或 IINA 命令；未命中交给插件输入管理器，再交给系统。
- 插件可以拦截 `keyDown`/`keyUp`。
- 按键绑定来源：内置 `iina/config/iina-default-input.conf`，用户可覆盖；设置里可编辑并检测冲突。
- IINA 命令以 `@iina` 前缀写在配置里，例如 `#@iina Shift+Meta+v video-panel`。

### 6.5 默认键位表（`iina/config/iina-default-input.conf`）

| 键 | 动作 | 说明 |
|---|---|---|
| Space | 播放/暂停 | `cycle pause` |
| ⌘. | 停止 | `stop` |
| ← / → | -5s / +5s | `seek` |
| ⌥← / ⌥→ | 上一帧/下一帧 | `frame-step` |
| ⇧← / ⇧→ | 字幕上一句/下一句 | `sub-seek` |
| ⌘S | 截图 | |
| ⌘L | A-B 循环 | |
| ⇧⌘L | 循环模式切换 | `cycle-values loop` |
| ⌘→ / ⌘← | 下一个/上一个媒体 | 播放列表 |
| ⇧⌘> / ⇧⌘< | 章节 +1 / -1 | |
| ⌘[ / ⌘] | 速度 ×0.5 / ×2 | |
| ⌥⌘[ / ⌥⌘] | 速度 ×1/1.1 / ×1.1 | |
| ⌘\ | 速度重置 1x | |
| ⌘0 / ⌘1 / ⌘2 | 窗口 0.5x / 1x / 2x | |
| ⌘- / ⌘= | 缩小/放大窗口 | |
| ⌃⌘P | 画中画 | |
| ⌃⌘F | 全屏 | |
| ⌃⌘T | 置顶 | |
| ⌥⌘M | 音乐模式 | |
| ⇧⌘T | 实时文字 | |
| ↑ / ↓ | 音量 +5 / -5 | |
| ⌥↑ / ⌥↓ | 音量 +1 / -1 | |
| ⌘/ | 静音切换 | |
| ⇧( / ⇧) | 音频延迟 ±0.5s | |
| ⌥⇧( / ⌥⇧) | 音频延迟 ±0.1s | |
| ⇧_ | 音频延迟归零 | |
| Z / X | 字幕延迟 -0.5 / +0.5 | |
| ⌥Z / ⌥X | 字幕延迟 -0.1 / +0.1 | |
| C | 字幕延迟归零 | |
| ESC / ENTER | 退出/进入全屏 | |
| q | 退出 | |
| p | 播放/暂停（mpv 兼容） | |
| . / , | 下一帧/上一帧（mpv 兼容） | |
| o / P | 显示进度 OSD | |
| m | 静音（mpv 兼容） | |
| ⇧PgUp / ⇧PgDn | ±600s | |
| G / F | 字幕放大/缩小 | |
| r / R / t | 字幕位置上/下 | |
| v / ⌥v | 主字幕/次字幕显隐 | |
| f | 全屏（mpv 兼容） | |
| E | 切换 edition | |
| 媒体键 | 播放/暂停/停止/下一首/上一首/音量/静音 | 可关闭 `useMediaKeys` |

### 6.6 菜单快捷键（与键位绑定相互独立）

| 菜单项 | 快捷键 |
|---|---|
| 打开… | ⌘O |
| 在新窗口打开… | ⌥⌘O（alternate） |
| 打开 URL… | ⇧⌘O |
| 在新窗口打开 URL… | ⌥⇧⌘O（alternate） |
| 设置… | ⌘, |
| 关闭 | ⌘W |
| 退出 | ⌘Q |
| 隐藏 IINA | ⌘H |
| 跳转到… | ⌘J |
| 截图 | ⇧⌘S |
| 播放历史 | ⇧⌘H |
| 视频滤镜… | ⇧⌘F |
| 音频滤镜… | ⇧⌘G |
| 最小化 | ⌘M |
| 检查器 | ⌘I |
| 日志查看器 | ⌘L |
| IINA 帮助 | ⌘? |

> 菜单项的快捷键会随用户键位绑定动态更新；上表是内置默认值（`iina/Base.lproj/MainMenu.xib`）。

---

## 7. 侧边栏

### 7.1 模型

- 左右两个槽位：`leading` / `trailing`，同一类型（settings/playlist/plugins）只出现在一侧，由设置决定默认侧。
- 每个槽位同一时间只装一个 `SidebarViewController`；显示新类型会替换当前内容。
- 三种类型：
  - `settings`：快速设置，4 个标签（布局/视频/音频/字幕），默认“视频”。
  - `playlist`：播放列表 + 章节，2 个标签，默认“播放列表”。
  - `plugins`：插件侧栏，单页。
- 可见性：`hidden / settings / playlist / plugins`；动画状态另记 `hidden/shown/willShow/willHide`。
- 行为：再次点击同一类型的同一标签 → 关闭；点击同类型不同标签 → 原地切标签；显示另一类型 → 替换。

### 7.2 几何与动画

- 宽度：默认 360，范围 300–500，且不超过窗口宽 80%；拖动内边缘 4pt 热区调整，松手保存。
- 容器：左右外贴窗口；显示时内边缘距视频 0；隐藏时整体滑出屏外（`constant = -width`）。
- 动画：0.24s `easeIn`；系统“减弱动态”时改为淡入；禁用动画时 0 时长。
- 显示/隐藏时同步调整标题栏、底部 OSC 的左右约束，并把标题文字容器贴到侧栏内边缘（`iina/SidebarController.swift:266-280`）。
- edge-to-edge 模式下，点击侧栏外的视频区域会关闭所有侧栏（单击且事件来自视频视图）；docked 模式不自动关闭。
- 浮动侧栏（未停靠）有 0.3 黑阴影 + 6pt 模糊；停靠时改画 1px 内边线。

### 7.3 标签栏

```
leading:  [96pt 空白] [分段控件（图标）]           [关闭 ←]
trailing: [关闭 →]    [分段控件（图标+选中文字）]
```

- 高 52（紧凑 48）；容器左右 10；leading 左侧额外 96 给红绿灯。
- 分段控件图标：布局 `paintbrush.fill`、视频 `photo`、音频 `waveform`、字幕 `captions.bubble.fill`；播放列表 `music.note.list`、章节 `list.and.film`；插件用 puzzlepiece。
- 选中态：圆角胶囊（半径 16，上下扩 4），灰色渐变 0.1→0.2，1px `SidebarTabBtnBorder` 描边；图标颜色 `SidebarTabTintActive`，未选中 `SidebarTabTint`。
- 文字只在 trailing 侧显示（选中标签名），leading 侧只显示图标，节省红绿灯空间。
- 关闭按钮：macOS 26 圆形玻璃，旧系统 `accessoryBar`；图标 leading 侧 `chevron.backward`、trailing 侧 `chevron.forward`。
- 标签切换：0.3s push 转场，方向由索引差决定；首次切换不动画。
- 触控板横滑切标签：水平位移 > 5 倍垂直位移且总位移 > 80 时触发（`iina/SidebarPane.swift:100-150`）。

### 7.4 卡片

- 侧栏内容是可滚动的垂直栈：外边距 16/14，卡片间距 18/16，卡片内边距 12/10。
- 卡片 = 圆角 8 的 `NSBox`：填充 `gray α0.1`，边框 1px `SidebarContainerBorder`，裁剪内容。
- 卡片内标题行：16pt 图标 + 12pt 粗体标题 + 弹性空白 + 右侧控件（如重置按钮）。
- 滑块行：标题行、滑块、刻度标签（10pt）、右侧数值输入框（50pt 宽）、悬停数值气泡（10pt）。
- 轨道选择器：下拉菜单列出轨道，首项是“无/默认”，选中项打勾；切换立即生效。

### 7.5 四个快速设置面板

| 标签 | 卡片顺序 | 主要控件 |
|---|---|---|
| 布局 | 主题/外观；视频布局；OSC 设置；侧栏设置 | 主题三选一、锁窗口宽高比、停靠 UI、去黑边、OSC 位置、自动隐藏、工具栏按钮、侧栏位置/宽度 |
| 视频 | 视频轨道；画面（宽高比/裁剪/旋转）；速度；开关（硬件解码/HDR）；均衡器 | 轨道下拉、宽高比下拉、裁剪下拉、旋转 0/90/180/270、速度滑块、HDR 开关、硬件解码开关 |
| 音频 | 音频轨道；音量；音频延迟；均衡器 | 轨道下拉、音量滑块、延迟滑块、10 段均衡器 |
| 字幕 | 主字幕轨道；次字幕轨道；加载字幕；位置/延迟；样式；ASS/图像字幕警告 | 轨道下拉、加载文件、位置滑块、延迟滑块、字体/大小/颜色/边框 |

- 卡片顺序即纵向顺序；每张卡片独立圆角、独立内边距。
- 字幕面板在有图像字幕（PGS/VobSub）时显示“无法调整样式”的警告卡片；ASS 字幕显示“部分样式由字幕决定”的警告。
- 均衡器是 10 段垂直滑块，横向滚动；启用开关在卡片标题行。

### 7.6 播放列表 / 章节

- 顶部工具栏：左 `[循环][随机][排序]`，中“总时长”，右 `[+][-][清空]`；高约 30/26 + 上下内边距 10/6。
- 表格：`fullWidth` 风格，行高 30（紧凑 26），水平网格线，背景 `SidebarTableBackground`，无表头。
- 行内容：
  - 左侧 16pt 选中/播放指示列。
  - 标题行（单行，中间截断）；第二行元数据（时长、分辨率、编码等，可开关 `playlistShowMetadata`）。
  - 正在播放的行显示进度条（`PlaylistProgressBar`）。
  - 音乐模式下可显示专辑/艺术家。
- 交互：双击播放；回车播放；拖拽重排（本地拖放类型 `iinaPlaylistItem`）；拖入文件/URL 追加；右键菜单 剪切/复制/粘贴/删除；多选；键盘删除走右键菜单同款命令。
- 章节标签：简单列表，行高相同，点击跳转，当前章节高亮；显示章节时间（`showChapterPos`）。

### 7.7 插件面板

- 单页，内容由插件提供；宿主只提供侧栏容器（宽度、圆角、滚动）。
- 插件可请求显示/隐藏、切换标签；权限在 `Info.json` 声明。

### 7.8 鼠标路由

- 主窗口收到鼠标事件先给 `SidebarController`：
  - 命中 4pt 缩放热区 → 开始拖拽调整宽度。
  - 侧栏可见且点击落在侧栏外 → 消费事件（edge-to-edge 下关闭侧栏）。
  - 其他情况交回主窗口。
- 侧栏可见时，其区域内的点击/滚动不应触发视频单击动作。

---

## 8. OSD

### 8.1 结构

```
┌─────────────────────────────────────┐  ← 圆角 16/10，内边距 16/8
│ [icon 0.8×字号]  [主文本 20pt 等宽]  │
│ [次要文本 10pt 次要色]               │
│ [进度条 12pt 高]                     │
│ [可选附件视图，最小宽 240]            │
└─────────────────────────────────────┘
```

- 垂直栈、左对齐、间距 0；标题行内部图标与文字间距 6。
- 主文本可含富文本（前后两段不同字号/颜色），例如“音量”小字 + “50%”大字。
- 进度条只在 `withProgress` / `withPosition` 类型显示；位置类消息强制从左到右。
- 图标是 SF Symbol，尺寸 = 文本字号 × 0.8。
- 次要文本字号 = 主字号 × 0.5，clamp 11–25；主文本字号来自设置 `osdTextSize`（默认 20）。

### 8.2 消息类型与模板

`OSDMessage` 是枚举，`titleAndType()` 返回（模板字符串，类型）（`iina/OSDMessage.swift:60-172`）。模板用 Mustache 渲染，可用变量：

| 变量 | 含义 |
|---|---|
| `{{duration}}` | 总时长 |
| `{{preciseDuration}}` | 总时长（3 位小数） |
| `{{position}}` | 当前位置 |
| `{{precisePosition}}` | 当前位置（3 位小数） |
| `{{percentPos}}` | 百分比位置 |
| `{{currChapter}}` | 当前章节序号（1 起） |
| `{{chapterCount}}` | 章节总数 |

类型：

| 类型 | 进度条 | 文本方向 | 典型消息 |
|---|---|---|---|
| `normal` | 无 | 跟随语言 | 暂停/继续、静音、截图、AB 循环、轨道切换 |
| `withText(text)` | 无 | 跟随语言 | 音量、速度、宽高比、裁剪、旋转、延迟 |
| `withProgress(value)` | 有 | 跟随语言 | 音量、速度（带进度） |
| `withPosition(value)` | 有 | 强制 LTR | 跳转、显示时间 |
| `withLeftToRightText(text)` | 无 | 强制 LTR | 带时间码的跳转提示 |

消息目录（按功能分组，均为 `OSDMessage` case）：

- 文件/播放：`fileStart`、`pause`、`resume`、`stop`、`seek`、`showTime`、`speed`、`abLoop`、`abLoopUpdate`、`chapter`、`track`、`playlist` 相关。
- 画面：`aspect`、`crop`、`rotate`、`deinterlace`、`hwdec`、`contrast`、`hue`、`saturation`、`brightness`、`gamma`、`addFilter`、`removeFilter`。
- 音频/字幕：`volume`、`mute`、`unMute`、`audioDelay`、`subDelay`、`subScale`、`subPos`、`subHidden/subVisible`、`secondSub*`。
- 截图/在线字幕：`screenshot`、`startFindingSub`、`foundSub`、`downloadingSub`、`downloadedSub`、`savedSub`、`cannotLogin`、`fileError`、`networkError`。
- 每条消息可被设置项单独关闭（`disableOSDFileStartMsg` 等）；错误类 `alwaysEnabled`。

### 8.3 显示/隐藏

- 同一时刻只显示一条；新消息覆盖旧消息并重置计时器。
- 显示：`isHidden = false`、alpha 1、按消息更新内容；自动隐藏计时 1s（可设置）。
- 隐藏：0.5s 淡出，然后 `isHidden = true`，移除附件视图，恢复同步计时器。
- 如果 OSD 正在显示，不重复淡入；只更新内容。
- 玩家可关闭 OSD（`enableOSD`），但错误类消息仍然显示。

### 8.4 持久 OSD（附件视图）

- 用于需要交互的内容（如插件、截图预览、在线字幕选择）。
- 附件最小宽 240、最小高 300；窗口若小于 500×300，先按比例放大到至少 500×300（0.3s 动画），附件从透明淡入。
- 持久 OSD 显示期间，普通 OSD 被抑制；ESC 交给 OSD 处理（可关闭）。
- 关闭时移除附件、恢复窗口尺寸（不强制缩回）。

### 8.5 位置与避让

- 默认右下：距右 ≥8、距底 ≥8；顶部与标题栏间距 8；左侧与侧栏间距 8。
- 全屏额外信息（时间/电量）在左下：距左 ≥8、距底 ≥8；与右侧 OSD 不重叠。
- 缓冲指示器居中；网络流打开时显示“正在打开流…”，缓冲时显示百分比与速度，跳转时显示“正在跳转…”。
- 时间预览/缩略图避让：OSD 覆盖进度条时不显示；侧栏覆盖时不显示；缩略图放不下时改到进度条下方。

---

## 9. 进度预览与缩略图

### 9.1 时间预览

- 半透明胶囊：liquid 圆角 16 / VEV 8，内边距 4，文本小号等宽数字。
- 内容：`章节名\n时间`；没有章节时只显示时间。
- 位置：
  - 底部 OSC：进度条上方 5pt。
  - 顶部 OSC：进度条下方 5pt。
  - 水平以鼠标位置居中，夹在窗口内。
- 只在鼠标悬停进度条时显示；离开进度条立即隐藏。

### 9.2 缩略图

- 4pt 圆角、1px 0.6 灰 α0.5 边框、2pt 黑阴影。
- 尺寸：宽取设置 `thumbnailWidth`（默认约 160），高按视频显示宽高比（考虑旋转/DAR）。
- 位置：优先在时间预览上方 5pt；若顶部超出窗口，改到进度条下方。
- 数据：播放器预生成缩略图（`generateThumbnails`），按秒取最近帧；未就绪时不显示。
- 拖动进度条时隐藏缩略图；OSD/侧栏覆盖进度条时也隐藏。

---

## 10. 迷你播放器（音乐模式）

### 10.1 窗口

- 独立窗口，隐藏系统红绿灯与标题；`isMovableByWindowBackground = true`。
- 高度 = 72 + 视频/封面高度；展开播放列表再 +300；列表自动收起阈值：窗口高 < 正常高 +200。
- 背景：`.underWindowBackground` 毛玻璃；视频/封面区域独立。
- 默认尺寸由 `setToInitialWindowSize` 计算；窗口缩放时保持视频宽高比。

### 10.2 布局

```
┌──────────────────────────────┐
│ [关闭/返回 12–13]            │
│                              │
│        专辑封面 / 视频        │
│                              │
│  标题 13pt 粗体（滚动）       │
│  艺术家 - 专辑 11pt 次要色    │
│  [当前] [==== 进度 ====] [总长]│
│  [音量][⏮][▶][⏭][列表][封面] │
└──────────────────────────────┘
┌──────────────────────────────┐
│ 播放列表（300 宽，可展开）     │
└──────────────────────────────┘
```

- 顶部左侧关闭按钮叠在封面左上：圆形半透明底、圆角 10、按钮 12/13；下方是“返回主窗口”按钮，间距 4。
- 信息区：标题居中、粗体 13；艺术家/专辑 11pt 次要色、居中；没有艺术家/专辑时标题下移 10pt。
- 进度条行：左右时间 32pt 最小宽，进度条贴底，左右各 6。
- 控制行高 48：静音、上一首、播放 28、下一首、列表 18、封面 18；间距 20/24。
- 控制区与信息区互斥淡入淡出：鼠标进入显示控制、隐藏信息；离开反之（0.2s）。

### 10.3 音量气泡

- 默认隐藏；点击静音按钮或滚轮调音量时从右侧展开。
- 容器宽 148（trailing 约束 10 → 148）、圆角 18、1px `SidebarContainerBorder`、黑色 0.5 阴影 2pt。
- 内部：静音按钮、音量条 100、数值标签 11pt。
- 触控板：手势开始展开、结束收起；鼠标：停止滚动 1s 后收起；鼠标移出容器立即收起。
- 展开/收起 0.2s 淡入淡出。

### 10.4 行为

- 列表按钮切换播放列表，状态持久化（`musicModeShowPlaylist`）；封面按钮切换视频/封面（`musicModeShowAlbumArt`）。
- 关闭窗口 = 退出音乐模式（主窗口不强制前置）并停止播放；左上返回按钮 = 退出音乐模式、显示主窗口并继续播放。
- 音频文件自动进入音乐模式（`autoSwitchToMusicMode`）；视频文件不自动进入。
- 主窗口进入音乐模式时，原窗口隐藏但播放器实例复用；退出时恢复原窗口和 UI 状态。

---

## 11. 全屏与画中画

### 11.1 全屏

- 触发：⌃⌘F、菜单、双击视频（默认）、绿色按钮、ESC/ENTER（键位）。
- 进入：窗口 frame 动画到屏幕 frame；标题栏切换为全屏形态；红绿灯/标题先隐藏后恢复；隐藏预览；关闭置顶；退出交互模式。
- 全屏 UI：鼠标移动显示 OSC；2.5s 后隐藏；标题栏在 OSC 顶部时随 UI 显隐；菜单栏自动隐藏。
- 全屏额外信息：设置开启时，左下显示时间 + 电量（18pt，时间粗体等宽）；不显示文件标题时只显示时间。
- 黑屏其他显示器：可选，创建覆盖其他屏幕的黑色无边框窗口。
- 退出：恢复 `priorWindowedFrame`；恢复菜单栏；恢复置顶与 UI。

### 11.2 画中画

- 使用系统 PiP（macOS `PIPViewController`）；进入时视频层交给 PiP 窗口，主窗口显示 PiP 占位遮罩，窗口本身按设置保持/隐藏/最小化。
- 进入/退出时强制重绘一帧，避免暂停状态出现白屏/黑屏。
- 全屏进入时若在 PiP 中，先退出 PiP。
- PiP 窗口移动屏幕时更新显示链路与 HDR 能力。
- 非 macOS 平台用各自 PiP API（Windows `AppWindow`/自定义置顶小窗，Android `PictureInPictureParams`，iOS `AVPictureInPictureController` 或自定义小窗）。

---

## 12. 设置窗口

### 12.1 结构

```
┌───────────────┬──────────────────────────────────┐
│ 搜索框         │  页面标题                         │
│ 页面列表       │  分区标题                         │
│  General       │  ┌ 行：图标 标签 ……… 控件 ┐       │
│  UI            │  └────────────────────────┘       │
│  Video         │  ...                              │
│  ...           │                                   │
└───────────────┴──────────────────────────────────┘
```

- 窗口 600×480，可缩放；左侧栏固定 200（不可折叠），内容最小 400。
- 左侧：`.sidebar` 材质，搜索框在顶部（macOS 26 上边距 40，旧系统 52），下面是源列表。
- 右侧：可滚动内容；页面切换即时替换内容，当前页面在侧栏高亮。
- 页面列表（顺序即侧栏顺序）：
  1. General
  2. UI
  3. Video
  4. Audio
  5. Subtitles
  6. Network
  7. Control
  8. Key Bindings
  9. Advanced
  10. Plugin
  11. Utilities
- 当前页面若有子分区，侧栏在该页下方展开小圆点 + 分区名列表，点击滚动到对应分区（0.25s，目标位于视口 33%）。

### 12.2 搜索

- 输入即搜索设置项标题/说明/页面；结果以 popover 列表显示（无动画）。
- 选择结果：切到对应页面并滚动到该项，随后画 1s 的高亮描边（4px 白 + 3px 强调色，圆角 8，前 80% 保持、后 20% 淡出）。
- 空输入关闭 popover。

### 12.3 行样式

- 行高由内容决定，垂直内边距 ≥8；左侧图标 24×20，文字距图标 30。
- 标题 13pt；说明 11pt 次要色，可多行；右侧控件（开关/下拉/文本框/按钮）右对齐，距右 8。部分数值输入框固定 64pt 宽。
- 开关用系统 switch；下拉用 popup；数值输入 64pt 宽；按钮为无边框文字按钮。
- 子项缩进 8，放在浅色子卡片中（圆角、内边距 12）。
- 不可用项置灰但保留布局，不隐藏（避免设置页跳动）。

---

## 13. 欢迎窗口

- 560×440，透明标题栏、可拖动背景；内容边距 30。
- 顶部左侧：应用图标 68、标题 “IINA” 20pt 粗体、版本号 11pt 次要色；右侧信息按钮（circular，橙色 info）。
- 操作区：右上两个大按钮“打开文件…”“打开 URL…”（large），水平间距 12。
- “继续播放”行：高 32，圆角，图标 `clock.arrow...`，文件名中间截断，右侧上次播放位置；有记录时才显示。
- 最近文件表：行高 28，无表头，透明背景；选中/悬停/按下分别用 0.10/0.20/0.25 黑（深色主题为白色 0.10/0.10/0.10）；双击打开。
- 背景：深色黑 0.4→0.1 渐变，浅色黑 0.1→0 渐变。
- 支持拖放文件/URL/字符串到窗口。
- Beta/Nightly/Debug 版本在版本号旁显示彩色标签；信息按钮弹出 240pt 宽的说明 popover。

---

## 14. 菜单系统

### 14.1 菜单树（`iina/Base.lproj/MainMenu.xib`）

| 顶级菜单 | 主要项 |
|---|---|
| IINA | 关于、检查更新、设置…、服务、隐藏、隐藏其他、全部显示、退出 |
| File | 打开…、在新窗口打开…、打开 URL…、在新窗口打开 URL…、打开最近、播放历史、在 Finder 中显示、删除当前文件、保存播放列表…、关闭、来自键位绑定的其他动作 |
| Edit | 撤销/重做、剪切/复制/粘贴/删除/全选、转换、语音 |
| Playback | 暂停、停止并清空播放列表、±5s、下一帧/上一帧、跳到开头、跳转到…、速度 ±、重置速度、截图、打开截图文件夹、A-B 循环、文件循环、显示播放列表、播放列表循环、下一首/上一首、显示章节、下一章/上一章 |
| Video | 显示视频面板、循环视频轨道、视频轨道、半/正常/双倍/适合屏幕、放大/缩小、PiP、全屏、置顶、锁定宽高比、实时文字、音乐模式、宽高比、裁剪、旋转、翻转、去隔行、去 Logo、视频滤镜…、已保存滤镜 |
| Audio | 显示音频面板、循环音频轨道、音频轨道、加载外部音频…、音量 ±5%/±1%、静音、音频延迟 ±0.5/±0.1、重置延迟、音频设备、音频滤镜…、已保存滤镜 |
| Subtitles | 显示字幕面板、循环字幕、字幕轨道、隐藏字幕、次字幕、隐藏次字幕、加载外部字幕…、在线字幕…、保存下载字幕…、编码、缩放 ±、重置缩放、延迟 ±0.5/±0.1、重置延迟、字体… |
| Plugin | 插件管理、插件菜单项（动态） |
| Window | 最小化、缩放、自定义 Touch Bar…、检查器、日志查看器、全部前置 |
| Help | IINA 帮助、导出调试信息、使用新设置窗口、版本亮点、GitHub、官网 |

### 14.2 动态菜单

- 打开最近：系统最近文档，带“清除菜单”。
- 播放列表菜单：当前播放列表条目，当前项打勾，点击跳转。
- 章节菜单：章节列表，当前章节打勾。
- 轨道菜单：视频/音频/字幕/次字幕，含“无”，当前轨道打勾。
- 滤镜菜单：已保存滤镜，动态重建。
- 键位绑定“其他动作”：把未出现在菜单里的键位绑定列出来，按分区折叠。
- 插件菜单：插件贡献的菜单项，显示快捷键。

### 14.3 启用规则

- 无活动播放器时，播放/视频/音频/字幕相关项禁用；文件相关项启用。
- 无字幕轨道时，字幕操作禁用。
- 播放列表为空时，下一首/上一首/循环禁用。
- PiP/全屏/音乐模式为勾选态或标题切换（如“进入全屏” ↔ “退出全屏”）。
- 置顶/锁定宽高比/循环等为勾选态。
- 菜单项标题随状态变化（暂停 ↔ 继续、显示面板 ↔ 隐藏面板）。

---

## 15. 插件 UI

### 15.1 全屏覆盖层

- 一个铺满视频区域的 WebView，默认隐藏；插件调用 `overlay` API 时显示。
- 点击穿透：宿主在 hit-test 时执行 JS `window.iina._hitTest(x, y)`，只有返回 true 才接收点击；否则事件穿透到视频。
- 简单模式：插件可设置一段 HTML/CSS，覆盖层以“样式 + 内容”方式渲染，切换时不闪白。
- 退出播放/切换文件时清空覆盖层。

### 15.2 侧栏

- 插件侧栏是 WebView，占满侧栏内容区；宽度/圆角/滚动由宿主提供。
- 插件可请求显示/隐藏侧栏，或切到自己的标签。

### 15.3 独立窗口

- 600×400，可缩放，标题 `Window — <插件名>`；WebView 铺满。
- 插件可设置窗口标题、尺寸、简单模式内容。

### 15.4 权限

- `Info.json` 声明 `overlay`、`sidebar`、`standaloneWindow`、`network-request`、`file-system` 等权限；未授权时 API 调用失败并在控制台报错。

---

## 16. 辅助窗口

| 窗口 | 尺寸/结构 | 说明 |
|---|---|---|
| 检查器 | 可缩放，分组表单 | 显示文件、视频、音频、字幕的详细属性；只读为主 |
| 视频/音频滤镜 | 可缩放，列表 + 编辑区 | 添加/删除/排序滤镜，保存预设 |
| 按键录制 | 小窗口 | 录制组合键，显示冲突检测 |
| 关于 | 固定大小 | 图标、版本、贡献者列表、致谢 |
| 日志查看器 | 可缩放 | 实时日志，级别过滤 |
| 打开 URL | 小窗口 | URL 输入、历史、请求头 |
| 字体选择 | 系统面板 | 字幕字体 |
| 插件商店 | 面板 | 插件列表、安装 |

这些窗口不参与播放 UI 的状态机；实现时保持“系统窗口 + 标准控件”即可，不必复刻 IINA 的自绘细节。

---

## 17. 文案与本地化

### 17.1 键名规范

| 前缀 | 用途 | 示例 |
|---|---|---|
| `menu.*` | 菜单项 | `menu.fullscreen` |
| `osc_toolbar.*` | OSC 工具栏 tooltip | `osc_toolbar.playlist` |
| `sidebar.*` | 侧栏标签/卡片标题 | `sidebar.aspect_ratio` |
| `quicksetting.*` | 快速设置项 | `quicksetting.hdr` |
| `settings.*` | 设置页 | `settings.theme` |
| `osd.*` | OSD 文案 | `osd.volume` |
| `mini_player.*` | 迷你播放器 | `mini_player.volume` |
| `main.*` | 主窗口/状态 | `main.buffering_indicator` |
| `touchbar.*` | Touch Bar | `touchbar.ahead_15` |

- 全部通过 `NSLocalizedString` 读取；中文在 `zh-Hans.lproj`，英文在 `en.lproj`。
- 文案里不要拼接句子；用带占位符的整句，例如 `main.buffering_indicator` = “缓冲中… %d%%”。

### 17.2 中文术语建议

| 英文 | 中文 |
|---|---|
| On Screen Controller | 屏幕控制器 |
| Picture in Picture | 画中画 |
| Music Mode | 音乐模式 |
| Playlist | 播放列表 |
| Chapters | 章节 |
| Inspector | 检查器 |
| Sidebar | 侧边栏 |
| Floating | 浮动 |
| Docked | 停靠 |
| Hardware Decoding | 硬件解码 |
| Tone Mapping | 色调映射 |
| Deinterlace | 反交错 |
| Delogo | 去 Logo |

---

## 18. 跨平台映射

### 18.1 概念映射

| macOS / AppKit | Windows（WinUI 3 / WPF） | Linux（GTK4 / Qt6） | Android（Compose） | iOS（SwiftUI/UIKit） |
|---|---|---|---|---|
| NSWindow + 自绘标题栏 | 无边框窗口 + 自绘标题栏（`ExtendsContentIntoTitleBar`） | `GtkHeaderBar` 或自绘 | 全屏 Activity + 自绘顶栏 | 全屏 VC + 自绘顶栏 |
| NSVisualEffectView | Mica/Acrylic（`DesktopAcrylicBackdrop`） | 合成器 blur / 半透明 | `RenderEffect` / scrim | `UIVisualEffectView` |
| SF Symbols | Segoe Fluent Icons | Adwaita / Material Symbols | Material Symbols | SF Symbols |
| NSMenu | MenuBar / CommandBar | GtkPopoverMenu | 下拉菜单/底部弹层 | UIMenu |
| NSToolbar | CommandBar | GtkHeaderBar | TopAppBar | NavigationBar |
| NSPopover | Flyout | GtkPopover | Popup/DropdownMenu | UIPopoverPresentationController |
| NSSlider | Slider | GtkScale | Slider | Slider |
| NSTableView | ListView | GtkListView | LazyColumn | List |
| NSTabViewController | TabView / Frame | GtkStack | 自绘分段 + AnimatedContent | TabView |
| NSDragging | DragDrop | GtkDragSource | DragAndDrop | UIDragInteraction |
| NSPIPViewController | 自定义置顶小窗 | 自定义小窗 | `PictureInPictureParams` | `AVPictureInPictureController` |
| NSTouchBar | 无（键盘快捷键替代） | 无 | 无 | 无 |
| Force Touch | 无（长按替代） | 无 | 长按/压感 | 3D Touch/长按 |
| 触控板横滑切标签 | 触控板手势 / 滚轮 | 触控板手势 | 左右滑动 | 左右滑动 |

### 18.2 交互替代规则

- **没有 hover**（移动端）：进度条预览改为“按住拖动时显示”；OSC 自动隐藏改为“点击视频切换显示/隐藏”，或播放中 3s 后隐藏。
- **没有右键**：长按弹出上下文菜单。
- **没有双击间隔**：移动端单击即暂停/显示控件，双击用于全屏（或不做双击）。
- **没有窗口**：全屏为默认；PiP/小窗为可选。
- **没有触控板**：滚轮跳转改为水平拖动进度条；音量用按键/滑块。
- **安全区**：iOS 顶部刘海/底部 Home 指示条，Android 手势条；OSC 底部内边距要加 safe-area inset。

### 18.3 视觉换算

- macOS pt ≈ Windows dp ≈ Android dp ≈ iOS pt；1x 下 1:1 映射。
- 字号按平台基准微调：Windows 建议 +0~1pt，Android 建议 +1sp，iOS 保持。
- 圆角：macOS 12/16 在 Windows 用 8/12，Android 用 12/16，iOS 用 12/16。
- 模糊不可用时用 85% 不透明背景 + 1px 边框替代。

---

## 19. 实施顺序与验收

### 19.1 建议顺序

1. **静态骨架**：主窗口 + 视频层 + 标题栏 + 空白 OSC（先不做交互），确认 edge-to-edge/docked 两种布局和窗口缩放。
2. **OSC**：三种位置、控件、显隐状态机、自动隐藏、拖动/吸附。
3. **播放控制**：播放/暂停、进度条、时间、音量、左右键、滚轮/键盘。
4. **侧栏**：容器、标签栏、四个快速设置页、播放列表/章节。
5. **OSD + 预览**：消息渲染、持久 OSD、时间/缩略图。
6. **模式**：全屏、迷你播放器、PiP。
7. **二级窗口**：设置、欢迎、插件、辅助窗口。
8. **打磨**：动画曲线、减少动态、RTL、键盘导航、无障碍标签。

### 19.2 验收清单

- [ ] 默认启动为深色、浮动 OSC、无侧栏、视频铺满。
- [ ] 鼠标进入窗口显示 OSC，2.5s 无操作隐藏；在 OSC 内移动不隐藏；离开窗口立即隐藏。
- [ ] 单击/双击/右键/中键行为与设置一致；双击不会同时触发两次单击。
- [ ] 浮动 OSC 可拖动，靠近中心吸附并触觉反馈，重启后位置恢复。
- [ ] 三种 OSC 位置切换后控件顺序、可见性优先级正确。
- [ ] 进度条拖动时暂停、松开恢复；悬停显示时间与缩略图；顶部 OSC 时缩略图在下方。
- [ ] 侧栏从正确一侧滑入，0.24s；再点同标签关闭；点外部关闭（edge-to-edge）。
- [ ] 四个快速设置页的卡片顺序、控件、即时生效正确。
- [ ] 播放列表拖拽重排、双击播放、右键菜单、章节跳转正确。
- [ ] OSD 单条覆盖、1s 自动隐藏、0.5s 淡出；持久 OSD 可交互并能 ESC 关闭。
- [ ] 全屏进出恢复窗口 frame、标题栏、置顶、UI 状态；ESC 退出。
- [ ] 迷你播放器高度、播放列表 300、音量气泡 148、控件 0.2s 显隐正确。
- [ ] 设置窗口 11 页、搜索跳转、分区指示器、行样式正确。
- [ ] 欢迎窗口 560×440、最近文件、继续播放、拖放正确。
- [ ] 键盘默认键位表全部可用；菜单快捷键不与键位冲突。
- [ ] 减少动态/禁用动画时无闪烁、无长动画。
- [ ] RTL 下进度条/音量条不镜像，侧栏与文本镜像。

---

## 20. 附录

### 20.1 常量速查

| 常量 | 值 | 来源 |
|---|---|---|
| `UIAnimationDuration` | 0.25 | `MainWindowController.swift:22` |
| `OSDAnimationDuration` | 0.5 | `MainWindowController.swift:23` |
| `SideBarAnimationDuration` | 0.24 | `SidebarController.swift:11` |
| 侧栏标签转场 | 0.3 | `SidebarPane.swift:21` |
| 迷你控件动画 | 0.2 | `MiniPlayerWindowController.swift:11` |
| 播放列表默认高 | 300 | `MiniPlayerWindowController.swift:10` |
| 播放列表自动收起阈值 | 200 | `MiniPlayerWindowController.swift:11` |
| 浮动 OSC 宽 | 460 | `OSCFloatingView.swift:12` |
| 浮动 OSC 估算高 | 67 | `OSCFloatingView.swift:117` |
| 标题栏基准高 | 32（macOS 26）/28 | `Titlebar.swift:9-17` |
| 文档图标起点 | 86 | `Titlebar.swift:24` |
| 主窗口最小 | 285×120 | `AppData.swift:72` |
| 无视频默认 | 640×360 | `AppData.swift:69-71` |
| 侧栏宽范围 | 300–500 | `SideBar.swift:80` |
| 设置窗口 | 600×480 | `SettingsWindow.swift:100` |
| 欢迎窗口 | 560×440 | `InitialWindowController.swift:87` |
| 插件窗口 | 600×400 | `PluginStandaloneWindow.swift:21` |
| OSC 自动隐藏 | 2.5s | `Preference.swift:1070` |
| OSD 自动隐藏 | 1s | `Preference.swift:1088` |
| OSD 字号 | 20（可设 1–500） | `Preference.swift:1089` |
| 进度条滑块 | 3×15 | `PlaySliderCell.swift:26-27` |

### 20.2 源码索引

| 主题 | 文件 |
|---|---|
| 主窗口/OSC/OSD/预览 | `iina/MainWindowController.swift` |
| 通用播放窗口/鼠标/滚动 | `iina/PlayerWindowController.swift` |
| 标题栏 | `iina/Titlebar.swift` |
| 底部 OSC | `iina/OSCBottomView.swift` |
| 浮动 OSC | `iina/OSCFloatingView.swift` |
| 工具栏按钮 | `iina/OSCToolbarButton.swift` |
| OSD 视图/消息 | `iina/OSDView.swift`、`iina/OSDMessage.swift` |
| 进度条 | `iina/PlaySlider.swift`、`iina/PlaySliderCell.swift`、`iina/PlaySliderLoopKnob.swift` |
| 音量 | `iina/VolumeSlider.swift`、`iina/VolumeButton.swift` |
| 侧栏容器/标签 | `iina/SideBar.swift`、`iina/SidebarController.swift` |
| 侧栏面板 | `iina/SidebarPane.swift`、`Sidebar*Pane.swift`、`SidebarSliderView.swift` |
| 迷你播放器 | `iina/MiniPlayerWindowController.swift` |
| 全屏/PiP | `iina/MainWindowController.swift`、`iina/VideoPIPViewController.swift` |
| 设置窗口 | `iina/SettingsWindow.swift`、`SettingsPage*.swift`、`SettingsItem.swift` |
| 欢迎窗口 | `iina/InitialWindowController.swift` |
| 菜单 | `iina/MenuController.swift`、`iina/MainMenuActions.swift`、`iina/Base.lproj/MainMenu.xib` |
| 默认键位 | `iina/config/iina-default-input.conf` |
| 颜色 | `iina/Assets.xcassets/*.colorset` |
| 插件 UI | `iina/PluginOverlayView.swift`、`PluginSidebarView.swift`、`PluginStandaloneWindow.swift` |

### 20.3 状态机伪代码

```text
# OSC 显隐
onMouseEnterWindow: showUI(); resetHideTimer()
onMouseMoveInWindow:
    showUI()
    if point in (OSC or titlebar): cancelHideTimer()
    else: resetHideTimer()
onMouseExitWindow: cancelHideTimer(); hideUI()
onHideTimerFired: hideUIAndCursor()
hideUI():
    if draggingOSC or inPIP or interactiveMode or liveText: return
    if !autoHideEnabled and !force: return
    state = willHide; animate alpha->0 (0.25s)
    onComplete: if state == willHide: setHidden(true); state = hidden
showUI():
    state = willShow; setHidden(false); animate alpha->1
    onComplete: if state == willShow: state = shown

# 侧栏
showSidebar(type, tab):
    if animating on target side: return
    if same type visible:
        if same tab: hideSidebar(); else switchTab(tab)
    else:
        if target occupied: replaceContent()
        else: slideIn(0.24s)
hideSidebar(side):
    state = willHide; slideOut(0.24s); onComplete: clearContent(); hidden

# 全屏
toggleFullscreen():
    if windowed: state = animating(true); save frame; animate to screen
    if fullscreen: state = animating(false); animate to saved frame
    onComplete: state = target; sync titlebar/OSC
```

### 20.4 已知平台差异与坑

1. **全屏动画期间不要重排视频**：IINA 在动画中只强制绘制，不改变视频约束；否则会闪烁。
2. **OSC 隐藏时红绿灯不要真隐藏**：用极小 alpha，避免系统按钮重新布局。
3. **窗口复用时重置标题**：否则 Window 菜单显示旧标题。
4. **RTL 不镜像时间轴**：进度条、音量条、播放控制簇固定 LTR。
5. **侧栏动画与标题栏约束联动**：只改侧栏宽度不同步标题栏，会出现标题被侧栏盖住。
6. **OSD 与进度条预览互斥**：OSD 盖住进度条时不要显示时间预览。
7. **Force Touch 只在 macOS 有**：其他平台用长按/压感/双击替代，不要留空手势。
8. **PiP 退出/进入要强制重绘**：暂停状态容易白屏/黑屏。
9. **减少动态不是“关动画”**：侧栏要从滑动改为淡入，避免 0 时长滑动闪烁。
10. **插件覆盖层要点击穿透**：否则会挡住视频的单击/双击。

---

*本规格基于 IINA `develop` @ `5c15f81` 的源码与资源整理；若上游 UI 重构，请以源码为准并更新本文件。*
