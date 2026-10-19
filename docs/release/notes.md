# Wiina v1.0.2 — release notes

Release date: 2026-10-19

Windows-native, IINA-inspired mpv front end built with WinUI 3, Windows App
SDK 2.5, and libmpv.

## What's new in 1.0.2

- **Floating OSC drag restored.** The real-video blur path no longer clears
  the drag area's background, so the whole floating OSC panel is draggable
  again (transparent hit-test brush instead of `null`).
- **Stable time/chapter previews.** The preview card resets its `Margin`
  before measuring, uses the clean `DesiredSize`, and is anchored to the
  pointer's actual X. This removes the horizontal drift left in 1.0.1, for
  both the video thumbnail and chapter cards.
- **4 px OSD rail.** At 100% display scaling, the horizontal OSD progress rail
  is 4 px tall with a 2 px corner radius.
- **Packaging correctness.** `scripts/package.ps1` now builds and copies the
  same `Platform=x64` output tree, rebuilds from source, and fails unless the
  packaged `Wiina.dll` reports the requested version. The 1.0.2 archive is
  verified as `1.0.2.0`, not a stale 1.0.1 payload.

## Highlights since 1.0.0

- **Real blur over video.** Titlebar, docked/top/floating OSC, sidebar, and
  OSD use a real Win2D Gaussian blur of the current libmpv frame (8 fps,
  320 px resample, translucent tint for legibility), not a flat tint.
- **Playback:** libmpv render API with a composition swapchain in a XAML
  `SwapChainPanel`; WinUI title bar, auto-hiding OSC, hover thumbnails,
  playlist/chapters/history, subtitles and track selection, playback speed,
  A-B loop, screenshots.
- **Windows shell:** Mica/Acrylic panels, 11-page settings with search,
  key-binding editor, menu, welcome window, about, log viewer, diagnostics
  inspector, open-URL dialog.
- **Modes:** fullscreen with chrome restore, PiP with main-window
  auto-minimize and one restore action, mini player, multiple windows,
  per-window state persistence.
- **Audio/video:** 10-band equalizer, audio/video filters, advanced A/V
  options, basic HDR metadata handling with SDR tone-mapping.
- **Polish:** runtime zh-Hans/English localization, accessibility and
  high-contrast support, reduced-motion support, six OSD positions, all-edge
  aspect-ratio lock, reproducible self-contained packaging, GPL/NOTICE
  compliance.

## Intentionally not included

- **Stage 9 (plugins) was skipped.** No plugin host, plugin API, `Info.json`
  parser, WebView2 dependency, or plugin settings that work. See
  [docs/plugins/README.md](../plugins/README.md).
- **Stage 10 (online subtitles / yt-dlp) was skipped.** No OpenSubtitles
  client, no yt-dlp integration, no bundled yt-dlp, no related permissions or
  network consent flows. Menu/settings entries remain disabled and say so.

## Install

Portable zip, no installer or separate runtime required:

1. Extract `wiina-1.0.2-win-x64.zip`.
2. Run `Wiina.exe`.
3. Data is stored in `%LOCALAPPDATA%\Wiina`.

An optional Inno Setup script lives at `installer/wiina.iss`; it is not built
or shipped in this repository.

## Package contents

```
Wiina.exe
Wiina.dll
libmpv-2.dll
Microsoft.Graphics.Canvas.dll
Microsoft.Graphics.Canvas.Interop.dll
LICENSE
NOTICE
README.md
SOURCE.txt
```

Additional .NET/WinUI publish output is included by the packager; `SOURCE.txt`
records the version, build date, commit, and source URL.

## Requirements

- Windows 11 22H2 or later (Windows 10 2004+ best effort)
- x64 CPU
- A GPU/driver with Direct3D 11 support

The build is self-contained; .NET 10 and Windows App Runtime do not need to be
installed separately.

## Upgrade / settings

Settings are stored in `%LOCALAPPDATA%\Wiina\settings.json`. Existing keys are
preserved; no destructive migration is performed. The `Language` field accepts
`system`, `zh-Hans`, and `en`.

## Known issues

See [known-issues.md](known-issues.md). Accessibility/localization details are
in [../accessibility.md](../accessibility.md); performance numbers in
[../performance.md](../performance.md). The blur trails the video by up to one
125 ms frame and is a 320 px resample; at 4K it costs more CPU than at 720p.
