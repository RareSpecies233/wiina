# Acrylic and video blur

Wiina uses the Windows App SDK acrylic backdrop for its secondary windows
(Settings, Welcome, Key Bindings) and in-app `AcrylicBrush` for the titlebar,
the top OSC strip and the floating/bottom OSC.

## What is real blur

- `Window.SystemBackdrop = DesktopAcrylicBackdrop` blurs whatever is **behind
  the window** on the desktop. It is real Gaussian blur and is what makes the
  Settings window match the Windows 11 context-menu material.
- `AcrylicBrush` blurs the XAML content painted **behind the brush inside the
  same window**. On a normal page this is real blur too.

Both are used here. The window tint (`WindowTintBrush`) is kept at roughly 40 %
alpha so the DWM acrylic is visible instead of being covered by an opaque
sheet, and the rail of the Settings window is a translucent wash over that same
backdrop rather than a second opaque panel.

## The video limitation

`SwapChainPanel` (used by the libmpv video host) is composited by a separate
swap chain. WinUI 3 does not expose those presented pixels to
`AcrylicBrush`, and there is no public `GaussianBlurEffect` in
`Microsoft.UI.Composition` in the Windows App SDK version Wiina targets. The
practical consequences are:

- An acrylic brush layered over the video renders as a flat translucent tint
  with noise, not as a blurred image. This is a documented WinUI limitation,
  not a Wiina bug
  ([microsoft-ui-xaml#9648](https://github.com/microsoft/microsoft-ui-xaml/issues/9648)).
- Producing a true blurred video backdrop would require reading the presented
  frames (Windows Graphics Capture or libmpv's render API into an app texture),
  running a Gaussian blur (Win2D/D3D11 shader) and compositing the result
  behind the controls. That is a separate, dependency-bearing rendering
  project; it is not implemented in v1 and no new NuGet package was added.

## Supported fallback

Over video, the titlebar and both OSC placements use a deliberately denser
tint (higher `TintOpacity`, a stronger `FallbackColor`, and a subtle hairline)
than the window surfaces. The controls stay legible on bright and dark footage,
and the same brushes are real acrylic whenever they sit over normal XAML
content instead of the swap chain. `docs/release/known-issues.md` records this
limitation for users.
