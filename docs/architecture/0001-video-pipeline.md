# ADR-0001：WinUI 3 中的视频呈现 —— mpv composition swapchain + SwapChainPanel

- 状态：**Proposed**（Stage 0 验证后改为 Accepted）
- 日期：2026-10-18
- 决策者：Wiina maintainers
- 相关：规格 §1.2、§3.3、§18.1；许可证审计 §4

---

## 1. 背景

IINA 在 macOS 上把视频渲染到独立的 `CAOpenGLLayer`/`CAMetalLayer`，UI 层用 AppKit 叠加。Windows/WinUI 3 没有等价的
“原生视频层 + XAML 覆盖”现成控件，必须选择一种嵌入方式。约束：

1. 播放内核必须是 mpv（用户要求），不能换成 Media Foundation/AV1 解码器。
2. UI 必须能浮在视频上（OSC、标题栏、OSD、侧栏），且不能出现 HWND airspace（子窗口永远盖住 XAML）。
3. 需要硬件解码、HDR 色调映射、字幕渲染、着色器能力，尽量不牺牲画质。
4. 必须是可持续维护的方案（不维护私有 mpv 分支）。

---

## 2. 候选方案

### A. `--wid` 子 HWND 嵌入（mpv 创建自己的窗口）

- 做法：把 WinUI 窗口的 HWND 传给 `--wid`，mpv 创建子窗口渲染。
- 优点：实现简单，mpv 官方支持，性能/兼容性最好。
- 缺点：**airspace**。子 HWND 永远绘制在 XAML 之上，OSC/侧栏/OSD 无法覆盖；除非把 UI 放到另一个分层窗口，
  复杂度和多显示器/DPI/全屏同步成本高。淘汰。

### B. libmpv Render API（OpenGL / 软件）

- 做法：`mpv_render_context_create` + `MPV_RENDER_API_TYPE_OPENGL`（ANGLE/EGL）或 `MPV_RENDER_API_TYPE_SW`，
  渲染到 FBO/内存，再呈现到 `SwapChainPanel`/位图。
- 优点：XAML 叠加天然可行；OpenGL 路径性能好。
- 缺点：官方 render API **没有 D3D11 后端**（mpv-player/mpv#5979 长期未合并）；Windows 上需要 ANGLE/EGL +
  `ISwapChainPanelNative` + 每帧 FBO 的复杂互操作；SW 路径 4K 性能不足。可作为备选。

### C. mpv 的 D3D11 composition 模式（选中）

mpv 0.41+ 新增：

```text
--d3d11-output-mode=composition
```

> “composition” will not create a window. If you want to use the D3D11 GPU backend in WinUI
> applications, you need to set this to “composition”. […] After creating the swapchain, you can get
> the swapchain address (int64 type value) by getting the `display-swapchain` property.
> — mpv manual, `--d3d11-output-mode`

配套：

- 只读属性 `display-swapchain`：D3D11 swapchain 的内存地址（int64）。
- 选项 `--d3d11-composition-size=<WxH>`：无窗口时必须由嵌入方告诉 mpv 输出尺寸（物理像素）。

做法：

1. `mpv_create` + `mpv_initialize`，参数：
   `--vo=gpu-next --gpu-context=d3d11 --d3d11-output-mode=composition --force-window=immediate`
   `--idle=yes --keep-open=yes --no-terminal --input-default-bindings=no --osc=no --osd-level=0`
2. 观察 `display-swapchain`；拿到非零地址后，通过 `ISwapChainPanelNative::SetSwapChain` 绑定到 XAML `SwapChainPanel`。
3. 窗口/DPI 变化时设置 `d3d11-composition-size`（物理像素），并用 `IDXGISwapChain2::SetMatrixTransform`
   把 buffer 像素映射到 DIP。
4. XAML 的 OSC/标题栏/OSD 等作为 `SwapChainPanel` 的兄弟节点，按 z-order 绘制在上层。

优点：

- 视频仍在 mpv 的 D3D11 渲染链里：硬件解码、gpu-next、着色器、字幕、HDR 色调映射全部保留。
- 没有 HWND airspace：`SwapChainPanel` 是 XAML 元素，参与 WinUI 合成。
- 不需要 ANGLE/EGL，不需要维护 mpv 补丁。
- 缩放/全屏只是 swapchain 尺寸变化。

缺点/限制：

- 要求 mpv ≥ 0.41（2025-12-21）；用户必须使用较新的 libmpv。
- `display-swapchain` 是只读属性，生命周期由 mpv 管理；窗口销毁顺序必须小心。
- 某些 GPU/驱动/远程桌面/虚拟机可能不支持 composition 模式，需要启动自检与回退。
- HDR 输出语义需在 Stage 8 单独验证（v1 先保证 SDR 色调映射正确）。

---

## 3. 决策

**采用方案 C**，Stage 0 在目标机器上做最小验证；若失败，回退方案 B（ANGLE/OpenGL Render API，参考 MIT 的
`Richasy/mpv-winui` 的 `ISwapChainPanelNative` + `MpvOpenGLFBO` 结构），再失败用 SW 上传做保底。

---

## 4. 关键互操作细节

### 4.1 绑定 swapchain

WinUI 3 的 `SwapChainPanel` 实现 `ISwapChainPanelNative`（`microsoft.ui.xaml.media.dxinterop.h`）：

```csharp
// IID_ISwapChainPanelNative
static readonly Guid IID = new("63aad0b8-7c24-40ff-85a8-640d944cc325");

nint unknown = ((WinRT.IWinRTObject)panel).NativeObject.ThisPtr;
Marshal.QueryInterface(unknown, in IID, out nint native);
// vtable: IUnknown[0..2], ISwapChainPanelNative::SetSwapChain = slot 3
var vtbl = *(void***)native;
var setSwapChain = (delegate* unmanaged[MemberFunction]<nint, nint, int>)vtbl[3];
setSwapChain(native, swapChainPtr);
```

- 必须在 UI 线程调用；`panel` 必须已加载。
- 解绑：`SetSwapChain(panel, 0)`，在销毁 mpv 之前。
- 不要 `Marshal.Release` 掉 mpv 的 swapchain（所有权在 mpv）。

### 4.2 DPI / 尺寸

`SwapChainPanel` 默认按 buffer 的像素尺寸当作 DIP 呈现；当 buffer 与面板 DIP 尺寸不一致时，用
`IDXGISwapChain2::SetMatrixTransform`：

```text
m11 = 1 / RasterizationScale, m22 = 1 / RasterizationScale
```

配合：

- `_committed`（buffer 当前尺寸）与 `_target`（宿主期望尺寸）；
- resize 时先把面板保持在 `_committed` 尺寸并用 `CompositeTransform` 缩放到 `_target`，等 mpv 完成新尺寸并呈现一帧后，
  再提交面板尺寸、复位 transform，避免黑闪；
- `WM_ENTERSIZEMOVE` 期间不请求 resize，`WM_EXITSIZEMOVE` 后一次性提交。

### 4.3 宽高比与黑边

- 窗口 `WM_SIZING` 锁定视频宽高比（整数像素误差可能产生 1px 黑边）。
- 当输出尺寸与视频比例误差 < 1% 时设 `keepaspect=no` 让 mpv 铺满；真正 letterbox 的场景保持 `keepaspect=yes`。

### 4.4 生命周期

```text
Window.Activated / VideoHost.Loaded
  → 创建 mpv（后台事件线程）
  → 观察 display-swapchain
  → SetSwapChain(panel, ptr)
  → 播放
Window.Closed
  → SetSwapChain(panel, 0)
  → mpv_destroy / 事件线程 join
  → 释放 D3D 资源
```

禁止在 mpv 事件线程直接碰 XAML；所有 UI 更新经 `DispatcherQueue`。

---

## 5. 验证计划（Stage 0）

| 项 | 方法 | 通过标准 |
|---|---|---|
| 基本呈现 | 播放 `testsrc` 1080p60 / 本地 H.264 | 画面正确、无花屏 |
| XAML 覆盖 | 视频上放红色 `Rectangle` + 文本 | 覆盖层可见且可交互 |
| resize | 拖动窗口 640×360 ↔ 1920×1080 | 无持续黑屏；允许一次瞬时缩放 |
| DPI | 100/125/150/200% | 画面不模糊、不偏移 |
| 全屏 | F11 / 双击 | 进入/退出正常，swapchain 尺寸正确 |
| 多显示器 | 拖到不同刷新率/HDR 屏 | 不崩溃；HDR 记录为已知限制 |
| 关闭 | 播放中关窗 | 无残留进程/线程/句柄 |
| 失败回退 | 强制禁用 composition | 给出明确错误，可切换备选 |

---

## 6. 参考实现（仅学习，不复制）

- `subdiox/nami`（GPL-3.0）：WinUI 3 + libmpv，README 明确使用 composition swapchain；`VideoView.cs` 的
  committed/target resize 方案与 `SwapChainPanelInterop.cs` 的 `ISwapChainPanelNative` 调用是同类问题的成熟解法。
- `Richasy/mpv-winui`（MIT）：OpenGL Render API + `ISwapChainPanelNative`，作为方案 B 参考。
- `henzfdev/HC-Player`：无许可证，**不参考代码**。

本项目将自行实现上述互操作（不复制源码），并在 Stage 0 记录与参考实现的差异。

---

## 7. 参考链接

- mpv manual, `--d3d11-output-mode`: https://mpv.io/manual/master/#options-d3d11-output-mode
- mpv manual, `display-swapchain`: https://mpv.io/manual/master/#properties
- mpv issue #5979（D3D11 render API 讨论）: https://github.com/mpv-player/mpv/issues/5979
- WinUI `SwapChainPanel`: https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.swapchainpanel
- `ISwapChainPanelNative`: `microsoft.ui.xaml.media.dxinterop.h`
