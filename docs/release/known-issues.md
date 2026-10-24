# Known issues — Wiina v1.2.3

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

## Chrome over video

- The titlebar, docked/top/floating OSC, sidebar, and OSD are **opaque by
  design**: a solid panel colour mixed with 16 % of the system accent
  (`ChromeSolidBrush` + `Services/ChromeTint.cs`). They never show the video
  behind them, and there is no live backdrop blur.
- The 1.0.0–1.2.2 capture pipeline (one libmpv frame every 125 ms, 320 px
  resample, Win2D Gaussian blur) was removed in 1.2.3. The reason and the
  migration notes are in
  [ADR-0004](../architecture/0004-solid-chrome.md); the old design is archived
  as superseded in [ADR-0003](../architecture/0003-acrylic.md).
- Because the tint follows the Windows accent colour live, changing the accent
  in Personalisation updates the chrome without a restart. High-contrast themes
  fall back to `SystemColorWindowColor`.

## File associations

- Registration is **per-user only** (`HKCU\Software\Classes`) and needs no
  administrator rights. It makes Wiina an eligible handler; Windows still asks
  the user to confirm a default app in **Settings → Apps → Default apps**, and
  Windows may reset defaults after a feature update.
- **Wiina never silently takes over a default handler.** The Settings page
  offers “关联视频格式 / 关联音频格式 / 关联全部支持格式”, a status line, an
  “在默认应用中确认…” shortcut, and “取消 Wiina 关联”.
- Unregistering removes only Wiina's own `Wiina.Video` / `Wiina.Audio` ProgIDs,
  its `OpenWithProgids` values, and its `RegisteredApplications` entry; other
  players' registrations are left alone.
- Older Windows 10 builds expose the same Default Apps page, but the exact
  layout differs.

## Windows 10 title bar

- `AppWindowTitleBar` customization is capability-guarded. On Windows 10 builds
  where the APIs are unavailable, Wiina keeps the system caption instead of the
  custom titlebar; the rest of the window still works, but the chrome looks
  different from Windows 11.

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
  The playback statistics card's row labels (文件 / 视频 / 音频 / 播放 / 渲染)
  are currently fixed Chinese strings; its title, subtitle, and close button
  are localized.
- No Arabic/Hebrew translation; RTL UI layout is unsupported. Video, OSC,
  timeline, and volume are pinned LTR so they never mirror.

## Accessibility

- No dedicated screen-reader live region; rapid OSD updates may not be
  announced.
- The key-binding recorder's status text is not announced automatically.
- Custom `FontIcon` glyphs are conveyed through the button's automation name,
  not as text.
- The settings rail no longer brightens on pointer hover; only the selected
  item is highlighted. Keyboard focus is still drawn.

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
  packaged build includes `libmpv-2.dll` next to `Wiina.exe`; replacing it with
  an incompatible newer mpv may break playback.
- Win2D is no longer a dependency, so no `Microsoft.Graphics.Canvas*.dll` ships.
- The packaged executable is unsigned; Windows SmartScreen may warn.

## Network

- Direct network URLs play through mpv, but there is no online-subtitle,
  metadata, or yt-dlp service integration (Stage 10 skipped). Proxy/user-agent
  settings affect mpv's own HTTP stack only.

## Media coverage

- Startup/memory numbers were measured with synthetic AVI clips (see
  `docs/performance.md`), not a representative codec matrix. Real-world 4K
  HEVC/AV1 files may use more memory/GPU and were not benchmarked here.
