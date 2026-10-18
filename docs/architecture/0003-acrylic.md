# Acrylic and video blur

Wiina has two blur stories:

1. **Secondary windows** (Settings, Welcome, Key Bindings) use
   `Window.SystemBackdrop = DesktopAcrylicBackdrop`. That is DWM blur of the
   desktop behind the window; it is unchanged.
2. **Chrome floating over the video** (titlebar, both OSC placements, the
   sidebar and the OSD panel) uses a real Gaussian blur of the *actual video
   frame*, produced by `src/Wiina.App/Interop/ChromeBlur.cs`. It is not a
   translucent color simulation.

## Why the XAML materials were not enough

`SwapChainPanel` (the libmpv host) is presented as a separate compositor
layer. WinUI 3 does not expose those pixels to `AcrylicBrush` /
`CompositionBackdropBrush`, and `Microsoft.UI.Composition` has no public
Gaussian blur effect. An acrylic brush over the video therefore rendered as a
flat translucent tint with noise (documented upstream in
[microsoft-ui-xaml#9648](https://github.com/microsoft/microsoft-ui-xaml/issues/9648)).
That was the v1 "fake blur" users could see.

## The implemented pipeline

`ChromeBlur` is a process-wide service:

- **Frame source**: every 125 ms (8 fps) a worker thread asks libmpv for the
  current frame with `MpvPlayer.ScreenshotRaw()` (`screenshot-raw`, `bgr0`).
  This is the real, presented video frame.
- **Reduction**: the frame is box-downscaled to 320 px wide on the worker
  thread, keeping the aspect ratio.
- **Blur**: `Microsoft.Graphics.Win2D` (NuGet `Microsoft.Graphics.Win2D`,
  1.3.x) runs `GaussianBlurEffect { BlurAmount = 9, BorderMode = Hard }` on a
  shared `CanvasDevice`, still off the UI thread.
- **Publish**: the blurred pixels are written into a `WriteableBitmap` on the
  UI thread and swapped into every attached layer in one pass.

`ChromeBlurLayer` is the per-surface presenter: a `Canvas` holding one
`Rectangle` filled by an `ImageBrush` (`Stretch=None`). A
`CompositeTransform` scales and translates the whole-frame bitmap so the part
under the surface lines up exactly with the video, and a rectangular `Clip`
keeps the (much larger) frame from painting outside the surface. Placement is
computed with `TransformToVisual` against the `MpvVideoView`, so docked,
floating, resized and multi-DPI layouts all stay aligned.

## Surfaces

| Surface | File | Corner radius |
| --- | --- | --- |
| Titlebar + top OSC row | `Controls/TitleBarView.xaml(.cs)` | 0 |
| Docked bottom OSC | `Controls/OscBottomView.xaml(.cs)` | 0 |
| Top OSD strip (`OscTop`) | same control as above | 0 |
| Floating OSC | `Controls/OscFloatingView.xaml(.cs)` | 8 |
| Sidebar | `Controls/SidebarHost.xaml(.cs)` | 0 |
| OSD message / persistent OSD panel | `Controls/OsdText` (upgraded at runtime by `ChromeBlur`) | 12 |

Each surface keeps its acrylic brush until the first blurred frame arrives;
`ChromeBlurLayer.FirstFrame` then hides the acrylic and shows a translucent
tint above the blur. The tint keeps text and icons legible without hiding the
blurred colors.

### Tints

Defined in `Themes/Tokens.xaml`:

- `TitleBarBlurTintBrush`: dark `#99101114`, light `#99F3F3F3` (~60 %).
- `OscBlurTintBrush`: dark `#B3101114`, light `#B3F3F3F3` (~70 %).
- `SidebarBlurTintBrush`: dark `#A6101114`, light `#A6F3F3F3` (~65 %).

High contrast maps to the system window color and the OSD panel keeps its
opaque system background, so the blur never weakens a high-contrast theme.

The OSD panel is not a chrome surface this service owns, so it is upgraded in
place: its background border receives the blur layer and tint, and the border
padding is moved into the message margin so the blur covers the whole rounded
panel. If the OSD later re-applies its padding, the injection re-normalises
it. This keeps `Controls/OsdText.*` free of blur code.

## Failure handling

Every path degrades to the previous acrylic look rather than crashing:

- Win2D device creation, frame capture, blur or publish failures are caught;
  the first trace goes through `Trace.WriteLine` (`[chrome-blur] ...`), later
  ones are dropped.
- If no frame ever arrives, `FirstFrame` never fires, the acrylic brushes stay
  and the tint layers stay collapsed.
- `WIINA_BLUR_DISABLE=1` disables the whole service for A/B comparison.
- Protected/DRM frames or an idle player simply produce no new frame; the last
  blurred frame stays visible.

## Cost

- One `screenshot-raw` per 125 ms: about 45 ms and 3.7 MB for a 1280x720
  frame, i.e. roughly a third of one core at 8 fps; 4K footage costs more
  because the copy is larger. The blur itself runs on the GPU.
- UI-thread cost is one 320-pixel-wide bitmap write plus an `ImageSource`
  swap per frame, shared by every surface.

## Known limits

- The blur trails the video by up to one 125 ms frame.
- The blurred backdrop is a scaled copy, so at very large window sizes the
  chrome shows a slightly softer version of the frame than the video itself.
- Nothing is captured when the player is idle, so the last frame's blur
  remains, matching the still video frame behind it.
