# Wiina v1.2.3 — release notes

Release date: 2026-10-24

Windows-native, IINA-inspired mpv front end built with WinUI 3, Windows App
SDK 2.5, and libmpv.

## What's new in 1.2.3

- **Quick Settings opens from every sidebar tab.** Clicking any Quick Settings
  tab while the sidebar shows Playlist, Chapters, or Plugins now switches the
  sidebar back to Settings and selects the requested page. Opening Quick
  Settings from the player and then pressing a tab no longer gets swallowed by
  the previous sidebar host.
- **Opaque, accent-tinted chrome (performance).** The per-frame video capture
  and Win2D Gaussian blur pipeline was removed. Titlebar, docked/top/floating
  OSC, sidebar, and OSD now paint an opaque panel colour mixed with 16 % of the
  system accent, and the tint follows accent changes live. Nothing over video
  is translucent any more; there is no full-resolution GPU readback per frame
  and no Win2D dependency.
- **Native playback statistics panel.** The statistics switch now opens a
  WinUI card instead of a one-second mpv `show-text` line. It appears
  immediately when enabled or at startup, refreshes once per second, groups
  rows under 文件 / 视频 / 音频 / 播放 / 渲染, and closes from its own button
  (the choice is remembered).
- **Mini player / PiP sizing and snapping.** The PiP video surface layout is
  decoupled from the 640×360 render cap, so growing the window now scales the
  picture instead of only enlarging the frame. Edge snapping moved to a shared
  helper (10 DIP snap distance, 22 DIP breakaway hysteresis, scaled to physical
  pixels): windows align flush with the monitor work area (the DWM border line
  and rounded corners are dropped only while snapped), any drag surface can
  snap, and the breakaway hysteresis makes a snapped window easy to pull off
  again.
- **Settings rail hover.** Unselected navigation items no longer brighten on
  hover or press; only the selected item keeps its highlight, with the keyboard
  focus rectangle preserved.
- **Readable quality presets.** The quality-preset manager replaces bare mpv
  property names with human-readable labels that state each option's trade-off
  and recommended role, with a short explanation per dropdown.
- **OSC auto-hide hardening.** Chrome now tracks open flyouts, re-arms after
  pointer release, guards the left-button-down state, hit-tests against actual
  opacity, and drives a single cancellable chrome storyboard; the statistics
  card counts as visible chrome. This fixes the intermittent "runs away"
  auto-hide.
- **File associations.** Settings gains a 12th page, 文件关联, that registers
  common video/audio extensions for the current user (HKCU), shows the current
  status, and can remove only Wiina's own ProgID / OpenWith entries. Windows
  still requires the user to confirm a default handler in Default Apps —
  Wiina never takes over silently. See [../file-associations.md](../file-associations.md).
- **WinUI 3 adaptation pass.** `AppWindowTitleBar` customization is capability-
  guarded before every call (Windows 10 falls back cleanly), the PiP window
  releases its theme/timer hooks, the statistics close choice persists, monitor
  fallback in the layout code is honest, and an open quality-preset window
  closes when the UI language changes.
- **Documentation.** README and the release/acceptance docs were re-audited
  against the code. ADR-0003 is archived as superseded and
  [ADR-0004](../architecture/0004-solid-chrome.md) records the solid-chrome
  decision; the 1.2.3 acceptance record lists all twelve user issues.

## Changes since 1.2.2 worth knowing

- The blur-performance-mode setting is gone. The capture/blur pipeline it
  controlled no longer exists, so old `settings.json` values for it are simply
  ignored. Chrome is always the opaque accent-tinted panel.
- The playback statistics card replaces the mpv OSD text. The separate
  dropped-frame switch still exists, but its transient `show-text` notice only
  fires while the statistics card is closed (the card already shows the counter).
- PiP/mini-player sizes restored from an older `settings.json` are still
  validated against `PipGeometry` / `MiniPlayerLayout`, and the mpv render
  buffer is clamped separately by `PipGeometry.RenderSize`.

## Intentionally not included

- **Stage 9 (plugins) is still skipped.** No plugin host, plugin API,
  `Info.json` parser, or WebView2 dependency. See
  [../plugins/README.md](../plugins/README.md).
- **Stage 10 (online subtitles / yt-dlp) is still skipped.** No OpenSubtitles
  client, no yt-dlp integration, no bundled yt-dlp. Menu/settings entries
  remain disabled and say so.
- **No silent default-handler takeover.** By Windows design, associating file
  types can only make Wiina an eligible handler; confirmation happens in
  Default Apps.

## Install

Portable zip, no installer or separate runtime required:

1. Extract `wiina-1.2.3-win-x64.zip`.
2. Run `Wiina.exe`.
3. Data is stored in `%LOCALAPPDATA%\Wiina`.

An optional Inno Setup script lives at `installer/wiina.iss`; it is not built
or shipped in this repository.

## Package contents

```
Wiina.exe
Wiina.dll
libmpv-2.dll
LICENSE
NOTICE
README.md
SOURCE.txt
```

Additional .NET/WinUI publish output is included by the packager; `SOURCE.txt`
records the version, build date, commit, and source URL. Win2D is no longer a
dependency, so no `Microsoft.Graphics.Canvas*.dll` ships.

## Requirements

- Windows 11 22H2 or later (Windows 10 2004+ best effort; titlebar
  customization degrades to the system caption where unsupported)
- x64 CPU
- A GPU/driver with Direct3D 11 support

The build is self-contained; .NET 10 and Windows App Runtime do not need to be
installed separately.

## Upgrade / settings

Settings are stored in `%LOCALAPPDATA%\Wiina\settings.json`. Existing keys are
preserved; no destructive migration is performed. Removed keys (such as the
old blur mode) are ignored on load. The `Language` field accepts `system`,
`zh-Hans`, and `en`.

## Known issues

See [known-issues.md](known-issues.md). Accessibility/localization details are
in [../accessibility.md](../accessibility.md); performance numbers in
[../performance.md](../performance.md). The chrome is opaque by design: it no
longer shows a blurred or translucent video background.
