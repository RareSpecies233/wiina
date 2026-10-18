# Wiina v1.0.0 — release notes

Release date: 2026-10-19

First v1 release: a Windows-native, IINA-inspired mpv front end built with
WinUI 3, Windows App SDK, and libmpv.

## Highlights

- **Playback**: libmpv render API with a composition swapchain in a XAML
  `SwapChainPanel`; WinUI title bar, auto-hiding OSC (bottom/floating/top),
  hover thumbnail previews, playlist/chapters/history, subtitles and track
  selection, playback speed, A-B loop, screenshots.
- **Windows shell**: Mica/Acrylic panels, 11-page settings with search,
  key-binding editor, menu, welcome window, about, log viewer, diagnostics
  inspector, open-URL dialog.
- **Modes**: fullscreen with chrome restore, PiP, mini player, multiple windows,
  per-window state persistence.
- **Audio/video**: 10-band equalizer, audio/video filters, advanced A/V options,
  basic HDR metadata handling with SDR tone-mapping.
- **v1 polish**:
  - runtime **zh-Hans / English** localization with an immediate, persisted
    language switch (510 catalog keys; settings, menus, OSD, dialogs, and shell).
  - accessibility: automation names for icon-only controls, keyboard focus,
    system high-contrast brushes, reduced-motion support.
  - reproducible self-contained packaging (`scripts/package.ps1`), SOURCE.txt,
    GPL/NOTICE compliance documentation.
  - measured startup/memory and documented DPI/HDR limits.

## Intentionally not included

- **Stage 9 (plugins) was skipped.** No plugin host, plugin API, `Info.json`
  parser, WebView2 dependency, or plugin settings that work. See
  [docs/plugins/README.md](../plugins/README.md).
- **Stage 10 (online subtitles / yt-dlp) was skipped.** No OpenSubtitles client,
  no yt-dlp integration, no bundled yt-dlp, no related permissions or network
  consent flows. Menu/settings entries remain disabled and say so.

These are scope decisions, not regressions; nothing in the release pretends
they exist.

## Install

Portable zip, no installer or separate runtime required:

1. Extract `wiina-1.0.0-win-x64.zip`.
2. Run `Wiina.exe`.
3. Data is stored in `%LOCALAPPDATA%\Wiina`.

An optional Inno Setup script lives at `installer/wiina.iss`; it is not built
or shipped in this repository.

## Package contents

```
Wiina.exe
libmpv-2.dll
LICENSE
NOTICE
README.md
SOURCE.txt
```

Additional publish output (WinUI/.NET runtime files) is included by the
packager; `SOURCE.txt` records the version, build date, commit, and source URL.

## Requirements

- Windows 11 22H2 or later (Windows 10 2004+ best effort)
- x64 CPU
- A GPU/driver with Direct3D 11 support

The build is self-contained; .NET 10 and Windows App Runtime do not need to be
installed separately.

## Upgrade / settings

Settings are stored in `%LOCALAPPDATA%\Wiina\settings.json` and were extended
with the `Language` field (`system`, `zh-Hans`, `en`). Existing keys are
preserved; no destructive migration is performed.

## Known issues

See [known-issues.md](known-issues.md). Accessibility/localization details are
in [../accessibility.md](../accessibility.md); performance numbers in
[../performance.md](../performance.md).
