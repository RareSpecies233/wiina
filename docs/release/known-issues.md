# Known issues — Wiina v1.1.1

Nothing here is hidden; these are known limitations of the v1 build.

## Scope decisions

- **Plugins (Stage 9) are not implemented.** There is no plugin API, no
  WebView2 host, and no working plugin installation. The Plugins settings page
  and menu item are disabled with a Stage 9 explanation. See
  [../plugins/README.md](../plugins/README.md).
- **Online subtitles / yt-dlp (Stage 10) are not implemented.** No
  OpenSubtitles search/download, no yt-dlp invocation, no bundled yt-dlp, no
  related network consent or permissions. The corresponding menu entries are
  disabled and say Stage 10.
- Optional: `installer/wiina.iss` exists for Inno Setup packaging; the portable
  zip remains the official v1 distribution. `scripts/release.ps1` compiles the
  installer only when Inno Setup 6 is installed locally. No MSIX package is
  produced.

## Blur over video

- Chrome over the video (titlebar, OSC, sidebar, OSD) uses a real Win2D
  Gaussian blur of the current libmpv frame, refreshed at 8 fps. It lags the
  video by up to one 125 ms frame and is a 320 px-wide resample, so it looks
  slightly softer than the video at very large window sizes. Capture costs
  roughly a third of one CPU core at 720p and more for 4K.
- Secondary windows keep the Windows desktop-acrylic material. See
  [../architecture/0003-acrylic.md](../architecture/0003-acrylic.md).

## HDR

- HDR content is tone-mapped to an **SDR 8-bit composition swapchain**. The app
  does not output true HDR10/FP16 to the display, and tone-mapping is not
  calibrated. See `docs/performance.md`.

## Localization

- Main shell and the settings window re-render immediately on language change.
  Already-open auxiliary windows (welcome, about, log viewer, inspector, filter
  window) pick up the new language when reopened.
- Inspector field names, codec/filter names, equalizer preset names, and raw
  logs intentionally remain in Chinese/technical form (documented exemption).
- No Arabic/Hebrew translation; RTL UI layout is unsupported. Video, OSC,
  timeline, and volume are pinned LTR so they never mirror.

## Accessibility

- No dedicated screen-reader live region; rapid OSD updates may not be
  announced.
- The key-binding recorder's status text is not announced automatically.
- Custom `FontIcon` glyphs are conveyed through the button's automation name,
  not as text.

## DPI / display / power

- Source uses DIPs and system resources, but 125/150/200 % and multi-monitor
  mixed-DPI moves were not captured as screenshots in this environment
  (changing system scaling requires a session restart). Treat as manual checks.
- Sleep/wake and monitor hot-plug rely on WinUI events and are not automated in
  this repository; verify on your hardware.
- HDR monitors: see HDR above.

## Packaging / runtime

- Portable, unpackaged app: no auto-update, no MSIX identity, no Store
  distribution.
- x64 only; ARM64 is unverified and not shipped.
- libmpv is dynamically linked and must match the pinned API level. The
  packaged build includes `libmpv-2.dll`; replacing it with an incompatible
  newer mpv may break playback.
- The packaged executable is unsigned; Windows SmartScreen may warn.

## Network

- Direct network URLs play through mpv, but there is no online-subtitle,
  metadata, or yt-dlp service integration (Stage 10 skipped). Proxy/user-agent
  settings affect mpv's own HTTP stack only.

## Media coverage

- Startup/memory numbers were measured with synthetic AVI clips (see
  `docs/performance.md`), not a representative codec matrix. Real-world 4K
  HEVC/AV1 files may use more memory/GPU and were not benchmarked here.
