# Known issues — Wiina v1.2.4

Nothing here is hidden; these are known limitations of the v1 build.

## Scope decisions

- **Plugins (Stage 9) are not implemented.** There is no plugin API, no
  WebView2 host, and no working plugin installation. The Plugins settings page
  is an honest placeholder and the menu item is disabled with a Stage 9
  explanation. See [../plugins/README.md](../plugins/README.md).
- **Online subtitles / yt-dlp (Stage 10) are not implemented.** No
  OpenSubtitles search/download, no yt-dlp invocation, no bundled yt-dlp, no
  related network consent or permissions. The corresponding menu entries are
  disabled and say Stage 10.
- **Two distribution forms, one payload.** `scripts/package.ps1` produces the
  portable zip with `Uninstall Wiina.exe`; `scripts/release.ps1` compiles
  `installer/wiina.iss` into a per-user installer when Inno Setup 6 is
  available. No MSIX package and no auto-update are produced. See
  [../installer.md](../installer.md).

## Chrome blur over video

- **The three glass modes cost a capture readback.** In 固定 / 动态 / 始终 the
  chrome samples one libmpv frame (`screenshot-raw`) every 125 ms (≈8 fps),
  box-downscales it to 320 px and blurs it with Win2D. That is roughly 45 ms
  and 3.7 MB per 1280×720 frame — about a third of one CPU core at 720p, more
  at 4K. **关闭毛玻璃（纯色）** does none of this: no capture thread, no Win2D
  device, only the opaque accent-tinted fill.
- **Fixed vs Dynamic vs Always**: Fixed captures one frame per chrome
  visibility cycle and holds it; Dynamic captures only while the chrome is
  visible; Always keeps capturing even when the chrome is hidden. In all three
  the blur trails the video by up to 125 ms and is a 320 px-scaled approximation.
- **Solid has no see-through by design.** The opaque `ChromeSolidBrush` (16 %
  system accent) is the point of that mode: it never shows the video behind it.
- **OSD message text stays opaque in every mode**, and a Docked layout keeps
  the titlebar opaque because its video is below the bar, not behind it.
- `WIINA_BLUR_DISABLE=1` forces the Solid fallback; Win2D/capture failures also
  degrade to Solid instead of crashing. The last blurred frame can stay visible
  while playback is paused or the player is idle.

## Statistics panel

- The native statistics card is now the only statistics surface; the
  persistent technical OSD feature was removed in 1.2.4.
- Its row labels and group names (文件 / 视频 / 音频 / 播放 / 渲染) are fixed
  Chinese strings; the card title, subtitle and close button are localized.
- The separate dropped-frame switch still exists and shows a transient
  `show-text` notice only while the card is closed.
- The card follows the OSD placement setting; moving it to a corner may
  overlap other chrome on small windows.

## Edge snapping

- Snapping uses the DWM **visible** frame, queried per window and cached per
  DPI (`WindowInterop.TryGetFrameMargins`). If the query fails (very old
  Windows builds, unusual theme hooks) the code falls back to zero margins,
  which restores the old small-gap behaviour rather than misplacing the window.
- 10 DIP snap / 22 DIP breakaway, scaled by the monitor DPI. Moving a window
  across monitors with **different** scale factors was reviewed in code but is
  still a manual check — the margin cache is keyed by DPI and invalidated on
  `WM_DPICHANGED`, not exercised as an automated test.
- Snapping applies to the native titlebar (`WM_MOVING`), the video-area drag,
  the mini player and PiP. Maximized/fullscreen windows are not snapped.

## File associations

- Registration is **per-user only** (`HKCU\Software\Classes`) and needs no
  administrator rights. It makes Wiina an eligible handler; Windows still asks
  the user to confirm a default app in **Settings → Apps → Default apps**, and
  Windows may reset defaults after a feature update.
- **Wiina never silently takes over a default handler.** The Settings page
  offers “关联视频格式 / 关联音频格式 / 关联全部支持格式”, a status line, an
  “在默认应用中确认…” shortcut, and “取消 Wiina 关联”.
- Unregistering removes only Wiina's own `Wiina.Video` / `Wiina.Audio` ProgIDs,
  their `OpenWithProgids` values, and its `RegisteredApplications` entry; other
  players' registrations are left alone.
- Older Windows 10 builds expose the same Default Apps page, but the exact
  layout differs.

## Installer and portable uninstaller

- The installer is unsigned, so SmartScreen may warn. It installs per user
  under `%LOCALAPPDATA%\Programs\Wiina`; a machine-wide install is not offered.
- The uninstaller asks 「是否保留配置文件？」 and defaults to **keep**; a silent
  uninstall also keeps settings. Only an explicit “否” (or
  `--remove-config`) deletes `%LOCALAPPDATA%\Wiina` and Wiina's own HKCU keys.
- The portable `Uninstall Wiina.exe` cannot delete its own running image, so it
  starts a detached batch file that retries for ~30 s. If another process holds
  a file in the folder (antivirus, an open log), deletion may fail and the exe
  reports that the folder must be removed manually (exit code 1).
- The installed copy intentionally does not contain `Uninstall Wiina.exe`;
  use Settings → Apps → Wiina → Uninstall there.

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
- No Arabic/Hebrew translation; RTL UI layout is unsupported. Video, OSC,
  timeline, and volume are pinned LTR so they never mirror.

## Accessibility

- No dedicated screen-reader live region; rapid OSD updates may not be
  announced.
- The key-binding recorder's status text is not announced automatically.
- Custom `FontIcon` glyphs are conveyed through the button's automation name,
  not as text.
- The settings rail: rest and hover are identical (dim), only the selected item
  is highlighted, and keyboard focus is a 2 px accent ring. This is deliberate;
  the rail does not change colour on pointer hover.

## DPI / display / power

- Source uses DIPs and system resources, but 125/150/200 % and multi-monitor
  mixed-DPI moves were not captured as screenshots in this environment
  (changing system scaling requires a session restart). Treat as manual checks.
- Sleep/wake and monitor hot-plug rely on WinUI events and are not automated in
  this repository; verify on your hardware.
- HDR monitors: see HDR above.

## Packaging / runtime

- Portable and installed forms are both unpackaged: no MSIX identity, no Store
  distribution, no auto-update.
- x64 only; ARM64 is unverified and not shipped.
- libmpv is dynamically linked and must match the pinned API level. The
  packaged build includes `libmpv-2.dll` next to `Wiina.exe`; replacing it with
  an incompatible newer mpv may break playback.
- Win2D ships again for the glass modes (`Microsoft.Graphics.Canvas.dll` and
  `Microsoft.Graphics.Canvas.Interop.dll`); Solid mode does not need it.
- Both executables are unsigned; Windows SmartScreen may warn.

## Network

- Direct network URLs play through mpv, but there is no online-subtitle,
  metadata, or yt-dlp service integration (Stage 10 skipped). Proxy/user-agent
  settings affect mpv's own HTTP stack only.

## Media coverage

- Startup/memory numbers were measured with synthetic AVI clips (see
  `docs/performance.md`), not a representative codec matrix. Real-world 4K
  HEVC/AV1 files may use more memory/GPU and were not benchmarked here.
