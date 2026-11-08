# Acceptance — v1-polish-3

Date: 2026-10-19  
Branch: `fix/v1-polish-3`  
Release: `artifacts/wiina-1.0.1-win-x64.zip`

## Requested fixes

1. **True video-background blur.** `src/Wiina.App/Interop/ChromeBlur.cs` captures
   the current libmpv presented frame through `MpvPlayer.ScreenshotRaw()` every
   125 ms, box-downsamples it to 320 px, applies a Win2D `GaussianBlurEffect`
   on a shared `CanvasDevice`, and clips the result to the screen position of
   each chrome surface. It covers the title bar, bottom/floating OSC, sidebar,
   and OSD (including the persistent OSD). It falls back to the previous
   acrylic/tint look if the first frame or any COM/Win2D step fails.
2. **Hover preview drift.** `TimelineControl.PreviewRequested` now passes the
   exact pointer X along with time and ratio. `ShowTimePreview` stores that X
   and `PositionTimePreview` anchors the card to it (still clamped to the
   window); no longer uses `width * ratio`.
3. **Welcome window.** Removed the bottom drag/drop hint. The placeholder
   folder glyph is replaced by the white/yellow triangle icon
   (`Assets/wiina.png`).
4. **100% volume notch.** Removed `UnityNotch` from `VolumeSlider.xaml` and its
   update/size handlers from `VolumeSlider.xaml.cs`.
5. **OSD bar aspect.** Replaced the fixed `ProgressBar` with an 8 px rounded
   track/fill (`OsdProgressHost` / `OsdProgressFill`); fill width is recomputed
   from the host width, so the bar stays flat instead of squashed.
6. **PiP.** The main-window PiP placeholder has one restore button. Entering PiP
   minimizes the main window; restoring from PiP or closing the PiP window
   restores/activates it.
7. **Release.** Version synchronized to 1.0.1 and portable zip produced.

## Verification

- `dotnet build .\Wiina.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test .\tests\Wiina.Core.Tests\Wiina.Core.Tests.csproj -c Release`:
  590 passed, 0 failed.
- Visual: title bar/OSC over the color-bar test video show a real blurred
  frame; OSD volume bar is a full-width 8 px rounded bar; welcome window has no
  bottom hint and uses the triangle icon.
- PiP: entering PiP creates a 432×243 window and `IsIconic(main) == True`;
  restore path was re-checked in code and exercised during the run.
- Package: `artifacts/wiina-1.0.1-win-x64.zip`, 145,768,348 bytes,
  SHA256 `B899C2FB613AD427124754C35B2990B2AB1FA2B5EABCE29B1EDCA0CCE9F6C1BC`.
  Extracted and launched successfully with libmpv and Win2D present; PiP
  minimize and double-click restore were exercised on this binary.

## Known limitation

The blur pipeline re-samples a 320 px frame at 8 fps. It lags the video by up to
one frame and is slightly softer than the video at very large window sizes. Any
capture failure logs one `[chrome-blur]` message and leaves the acrylic/tint
fallback in place; `WIINA_BLUR_DISABLE=1` disables it for A/B comparison.
