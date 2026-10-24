# ADR-0003: Acrylic and video blur

- **Status: Current** (accepted 2026-10-18 for 1.0.0; restored and extended in
  v1.2.4 after the short-lived 1.2.3 solid-only experiment)
- Related: [ADR-0004 — Solid mode](0004-solid-chrome.md) (the "no blur"
  option), `src/Wiina.App/Interop/ChromeBlur.cs`,
  `src/Wiina.Core/Settings/ChromeBlurMode.cs`, `Services/ChromeTint.cs`

Wiina has two blur stories:

1. **Secondary windows** (Settings, Welcome, Key Bindings) use
   `Window.SystemBackdrop = DesktopAcrylicBackdrop`. That is DWM blur of the
   desktop behind the window; it is unchanged.
2. **Chrome floating over the video** (titlebar, both OSC placements, the
   sidebar) uses a real Gaussian blur of the *actual video frame*, produced by
   `src/Wiina.App/Interop/ChromeBlur.cs`. It is not a translucent color
   simulation, and it is **optional**.

## Why the XAML materials were not enough

`SwapChainPanel` (the libmpv host) is presented as a separate compositor
layer. WinUI 3 does not expose those pixels to `AcrylicBrush` /
`CompositionBackdropBrush`, and `Microsoft.UI.Composition` has no public
Gaussian blur effect. An acrylic brush over the video therefore rendered as a
flat translucent tint with noise (documented upstream in
[microsoft-ui-xaml#9648](https://github.com/microsoft/microsoft-ui-xaml/issues/9648)).
That was the v1 "fake blur" users could see.

## The four modes

`ChromeBlurMode` (`src/Wiina.Core/Settings/ChromeBlurMode.cs`) is exposed as
Settings → 界面 → 窗口与 OSC → 毛玻璃模式 (row id `ui.blur-mode`):

| Mode | Enum | Behaviour | Capture work |
| --- | --- | --- | --- |
| 固定（静态帧） | `Fixed = 0` | Capture one frame when the chrome becomes visible, reuse it until the chrome hides | one frame per show |
| 动态（跟随视频） | `Dynamic = 1` (default) | Capture while the chrome is visible and follow the video | ~8 fps while visible |
| 始终（持续计算） | `Always = 2` | Keep calculating even while no chrome is visible | ~8 fps while the player runs |
| 关闭毛玻璃（纯色） | `Solid = 3` | No capture and no blur; opaque accent-tinted panel | none |

**Start/stop.** `ChromeBlur.SetActive(true)` is called from `ShowChrome` and
`SetActive(false)` from `HideChrome` (`MainWindow.xaml.cs`). The worker loop
parks while `!_active` for Fixed/Dynamic, ignores visibility for Always, and
`Fixed` additionally parks once `_fixedCaptured` is set. Showing the chrome
again resets `_fixedCaptured`, so Fixed takes a fresh still. In Solid the
worker is never started at all.

**Solid bypass.** Solid does not create `CanvasDevice`, does not start the
worker, and drops any in-flight frame. Each surface instead paints the opaque
`ChromeSolidBrush` (`Themes/Tokens.xaml`: dark `#FF15171B`, light `#FFF3F3F3`,
high contrast `SystemColorWindowColor`), mixed 16 % toward the system accent by
`Services/ChromeTint.cs`. `ChromeTint` subscribes to
`UISettings.ColorValuesChanged` and re-mixes on the UI thread, so changing the
Windows accent updates the chrome without a restart. A 1.2.3 settings file
that stored `Solid` keeps Solid; the enum value is unchanged.

`WIINA_BLUR_DISABLE=1` makes `TryAttach` fail, which keeps every surface on
the solid brush — a support/A-B switch that needs no settings change.

## The implemented pipeline (glass modes)

`ChromeBlur` is a process-wide service:

- **Frame source**: every 125 ms (8 fps) a worker thread asks libmpv for the
  current frame with `MpvPlayer.ScreenshotRaw()` (`screenshot-raw`, `bgr0`).
  This is the real, presented video frame.
- **Reduction**: the frame is box-downscaled to 320 px wide on the worker
  thread, keeping the aspect ratio.
- **Blur**: `Microsoft.Graphics.Win2D` (NuGet `Microsoft.Graphics.Win2D`,
  1.3.2) runs `GaussianBlurEffect { BlurAmount = 9, BorderMode = Hard }` on a
  shared `CanvasDevice`, still off the UI thread.
- **Publish**: the blurred pixels are written into a `WriteableBitmap` on the
  UI thread and swapped into every attached layer in one pass.

`ChromeBlurLayer` is the per-surface presenter: a `Canvas` holding one
`Rectangle` filled by an `ImageBrush` (`Stretch=None`). A
`CompositeTransform` scales and translates the whole-frame bitmap so the part
under the surface lines up exactly with the video, and a rectangular `Clip`
keeps the (much larger) frame from painting outside the surface. Placement is
computed with `TransformToVisual` against the `MpvVideoView`, so docked,
floating, resized and multi-DPI layouts all stay aligned. Layers are
effect-only and never take input.

## Surfaces

| Surface | File | Corner radius | Notes |
| --- | --- | --- | --- |
| Titlebar + top OSC row | `Controls/TitleBarView.xaml(.cs)` | 0 | stays opaque in Docked layout |
| Docked bottom OSC | `Controls/OscBottomView.xaml(.cs)` | 0 | |
| Floating OSC | `Controls/OscFloatingView.xaml(.cs)` | 8 | |
| Sidebar | `Controls/SidebarHost.xaml(.cs)` | 0 | |
| OSD message | `Controls/OsdText` | — | **not blurred**; opaque in every mode |

Each surface keeps its opaque brush until the first blurred frame arrives;
`ChromeBlurLayer.FirstFrame` then hides it and shows a translucent tint above
the blur. The titlebar's blur is suppressed in the Docked layout because the
video sits below the bar there, so a blurred overlay would not match what is
behind it.

### Tints

Defined in `Themes/Tokens.xaml`:

- `TitleBarBlurTintBrush`: dark `#99101114`, light `#99F3F3F3` (~60 %).
- `OscBlurTintBrush`: dark `#B3101114`, light `#B3F3F3F3` (~70 %).
- `SidebarBlurTintBrush`: dark `#A6101114`, light `#A6F3F3F3` (~65 %).

High contrast maps these to the system window color (the sidebar tint is
transparent in high contrast), and the OSD panel keeps its opaque system
background, so the blur never weakens a high-contrast theme.

## Failure handling

Every path degrades to the solid chrome rather than crashing:

- Win2D device creation, frame capture, blur or publish failures are caught;
  the first trace goes through `Trace.WriteLine` (`[chrome-blur] ...`), later
  ones are dropped. Set `WIINA_BLUR_LOG=1` to emit the diagnostic trace.
- If no frame ever arrives, `FirstFrame` never fires and the solid brush stays.
- `WIINA_BLUR_DISABLE=1` disables the service for A/B comparison.
- Protected/DRM frames or an idle player simply produce no new frame; the last
  blurred frame stays visible.
- Switching modes live is supported: surfaces listen to `ChromeBlur.ModeChanged`
  and attach (leaving Solid) or fall back to the solid brush.

## Cost

- One `screenshot-raw` per 125 ms: about 45 ms and 3.7 MB for a 1280x720
  frame, i.e. roughly a third of one core at 8 fps; 4K footage costs more
  because the copy is larger. The blur itself runs on the GPU.
- UI-thread cost is one 320-pixel-wide bitmap write plus an `ImageSource`
  swap per frame, shared by every surface.
- `Solid` has none of these costs: no worker, no Win2D device, no readback.
- Snapping is unrelated to this pipeline: it queries DWM frame margins only on
  move/DPI change, not per frame.

## Known limits

- The blur trails the video by up to one 125 ms frame.
- The blurred backdrop is a scaled copy, so at very large window sizes the
  chrome shows a slightly softer version of the frame than the video itself.
- Nothing is captured when the player is idle, so the last frame's blur
  remains, matching the still video frame behind it.
- OSD message text is opaque in all modes; in the Docked layout the titlebar is
  opaque as well.
- Solid mode is intentionally see-through-free: it is a flat panel, not a
  cheap blur.
