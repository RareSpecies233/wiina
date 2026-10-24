# Known issues — Wiina v1.3.0

Nothing here is hidden; these are known limitations of the v1.3.0 build.

## Settings layout in 1.3.0

The 1.3.0 reshuffle is a layout change, not a behaviour change: row ids and
settings keys are unchanged, so existing `settings.json` files and deep links
keep working. The snapshot in `../reference/settings-layout.md` is the
authoritative map; update it whenever the catalog changes.

## Key binding editor

- One chord per action; modifier+key only, no multi-stroke sequences.
- Conflict handling is Wiina's own (mpv itself is last-wins). IINA's editor has
  no conflict detection or export, so the IINA preset is a best-effort
  translation; `../reference/iina-shortcuts.md` lists every deviation.
- Built-in profiles are read-only by design; edits always land in a custom
  profile.

## System media keys

SMTC integration means Windows can route media keys to Wiina only while Wiina
is running. There is no per-device testing matrix here; hardware media keys
were verified on one keyboard.

## mpv.net configuration export

`resources/mpvnet/` is a plain-text export of options and bindings. It is
version-specific (mpv.net bundles its own libmpv), Wiina-only features are
absent, and the Wiina side-panel commands are commented out because mpv.net has
no side panels. Nothing is installed or patched by Wiina.

## Frame-drop OSD

Dropped-frame numbers come from mpv counters and are estimates; the rate is
measured over a 3-second window with a 12-second cooldown, so a single bad
frame does not spam the OSD. The statistics panel remains the detailed view.

## Cursor auto-hide does not work under WinUI 3 (1.3.0)

Status: known limitation, root-caused, not fixed in 1.3.0.

The desktop pointer over a WinUI 3 client area is driven by CoreMessaging, not
by the window procedure or the XAML thread. Three mechanisms were implemented
and verified inert on a real 1080p playback session:

- `WM_SETCURSOR` override plus subclassing every WinUI cursor host
  (`Microsoft.UI.Content.DesktopChildSiteBridge`, `InputSiteWindowClass`,
  `InputNonClientPointerSource`): 3 windows subclassed, 0 messages received.
- A thread-level `WH_CALLWNDPROCRET` hook on the input/XAML thread: hook
  installed, 0 pointer messages observed.
- Reflected `UIElement.ProtectedCursor` on `RootGrid`, `VideoHost` and the inner
  `SwapChainPanel`: the setter succeeds, but `null` means "default arrow" in
  this WinAppSDK and `InputSystemCursorShape` has no "none" member.

`GetCursorInfo` keeps returning the arrow (0x10019) immediately after a
successful `SetCursor(blank)` on every candidate thread, so it cannot be used as
an oracle; screenshots never contain the OS pointer either.

Next route (requires a resource/csproj change): embed a 1×1 transparent `.cur`,
load it via `InputDesktopResourceCursor.CreateFromResourceId`, and assign it to
`ProtectedCursor`; alternatively adopt a WinAppSDK that exposes
`InputCursor.CreateFromVisual`. Until then, use the Windows pointer settings if
an always-hidden pointer is required.

## Slow thumbnail mode

Single-worker mode trades first-hover latency for lower CPU: pre-generation is
skipped, and each hovered bucket is generated on demand (cache hits are still
instant). The default mode (pre-generation) is unchanged.
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

## OSC auto-hide and placement

- **The delay is configurable only in non-docked layouts.** Settings → 界面 →
  窗口与 OSC → 自动隐藏延时 accepts 0.5–30 s (default 2.5). Docked layout keeps
  its titlebar and bottom bar permanently visible by design.
- **Sub-5-DIP jitter does not count as activity.** A hand resting on the mouse
  no longer keeps the chrome awake, and a pointer resting over the fullscreen
  bottom bar hides on schedule. If that is undesirable (screen readers, remote
  desktop), disable auto-hide instead of holding the pointer over the bar.
- **Deactivation hides immediately** except in Docked layout or while a sidebar
  is open. A left click on the video hides the chrome only when press and
  release stay put; a drag never hides it.
- **The floating OSC avoids a sidebar only when it would be covered.** The
  clamp uses the rectangle of the visible leading/trailing panel and an 8 DIP
  gap; panels that do not vertically overlap the OSC leave it untouched. PiP
  and mini layouts never show an OSC.

## Timeline thumbnails

- Thumbnails are now pre-generated after a file loads and cached on disk
  (`%LOCALAPPDATA%\Wiina\thumbnails\v2`, format v2): one JPEG per 5 s bucket plus
  an atomically rewritten `index.json`; the default budget is 512 MB (Ui →
  Thumbnails → Thumbnail cache, 0 disables it) and eviction is byte-based LRU.
  Hovering reads disk/memory first; misses are queued to the single extraction
  worker and jump ahead of the pre-generation backlog.
- Limits: the cache key includes file size and mtime, so an in-place replacement
  with identical size/mtime can hit a stale frame; dragging the timeline is not
  throttled (only stale results are dropped); network/UNC sources are
  memory-only; old files are reclaimed lazily by the LRU trim.
- Measured on a 1080p 23:40 HEVC file: cold pre-generation 14.1–14.9 s for 285
  buckets, warm start 0.3 s, hover 73 ms cold / 4 ms warm, 1.70 MB on disk.
  Research comparison with IINA: [../reference/iina-thumbnails.md](../reference/iina-thumbnails.md).
## Audio output device

- The dropdown is fed by mpv's `audio-device-list`. Before the first player
  starts it contains only "跟随系统默认"; the refresh button re-reads the cached
  list rather than polling mpv.
- A saved device mpv no longer reports is shown as "不可用（<name>）" and kept
  verbatim until you pick another device, so settings are never silently
  rewritten.
- Device labels are mpv's descriptions; the raw name (`wasapi/...` on Windows)
  is only in the tooltip. Changing devices mid-playback re-initialises the audio
  output, which can produce a short glitch or a brief silence.

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
  system accent, dark base `#FF202227` since 1.2.5) is the point of that mode:
  it never shows the video behind it. Since 1.2.5 the panels do not assign
  `Background` from code at all - the blur/tint layers are children toggled by
  `Visibility` - so Solid cannot become translucent.
- **The OSD card follows the same mode**: glass in 固定 / 动态 / 始终, opaque in
  Solid, and it never takes input. A Docked layout keeps the titlebar opaque
  because its video is below the bar, not behind it.
- `WIINA_BLUR_DISABLE=1` forces the Solid fallback; Win2D/capture failures also
  degrade to Solid instead of crashing. The last blurred frame can stay visible
  while playback is paused or the player is idle.

## Live theme switching

- Settings, the quick-settings menu and the player window now apply a theme
  change immediately in every open window (player titlebar + floating OSC,
  settings, sidebar, PiP, about, log viewer, inspector, filters, key bindings,
  welcome, quality editor). `ThemeMode.System` follows the current Windows app
  theme; the WinUI `ElementTheme.Default` value does not, so the app resolves a
  concrete dark/light value itself.
- Limitation: `DesktopAcrylicBackdrop` (Mica/acrylic) window material may lag one
  paint behind the XAML content on some systems. Switching between light and dark
  while **High contrast** is on was not visually verified in this cycle.
- The change is a one-off recolour of the chrome brushes plus a theme push per
  window; it does not affect measured playback cost.

## Quality presets

- The editor exposes **23 mpv video-quality options** in seven groups (缩放 /
  色度 / 抖动 / 去色带 / 插帧 / 同步 / 其他). Values are stored as raw mpv
  strings, so a profile can name an option this build does not know; unknown
  imported keys are preserved in JSON and shown as “未识别（保留原值）”, but are
  deliberately **not applied**.
- Only two built-in presets exist: **默认** and **性能优先**. Legacy names
  (`自动（IINA 默认）`, `均衡`, `高质量`, enum values `Balanced` /
  `HighQuality`) normalize to 默认 on import.
- The **去色带** master switch lives in 设置 → 视频 → 画面 (`video.deband`,
  default off). The profile deband iterations/threshold/range/grain are always
  saved but reach mpv only while that switch is on.

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

- Two modes exist. **Per-user** (default, no admin): `HKCU\Software\Classes`
  ProgIDs, `OpenWithProgids`, Capabilities and `RegisteredApplications`.
  **Machine-wide** (Settings → File associations → "Associate as
  administrator…"): relaunches Wiina elevated and writes the same structures
  under `HKLM\Software\Classes`, then best-effort clears the per-extension
  `UserChoice`. Both make Wiina an eligible handler; Windows still asks the user
  to confirm in **Settings → Apps → Default apps**, and defaults may be reset
  after a feature update.
- **Wiina never silently takes over a default handler.** The page offers
  associate video / audio / all, a status line, "在默认应用中确认…", unregister,
  and the admin action. Dismissing the UAC prompt is handled as a cancellation,
  not an error loop.
- Unregister removes only Wiina's own `Wiina.Video` / `Wiina.Audio` ProgIDs,
  their `OpenWithProgids` values, `RegisteredApplications\Wiina` and
  `HKCU\Software\Wiina`, in both HKCU and (when removable) HKLM. Other players'
  registrations are untouched. `UserChoice` is a protected key: if Windows
  denies the delete, the system default is simply left for the user to change.
- Machine-wide registration affects every user on the PC; portable copies that
  move after registering keep stale paths until re-registered or unregistered.
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
- The quality editor window grew to 23 options; its 9 numeric fields are compact
  `NumberBox` controls with spin buttons (1.2.5 fixed a layout bug where the
  spinner column was missing and the buttons overlapped the label).

## Network

- Direct network URLs play through mpv, but there is no online-subtitle,
  metadata, or yt-dlp service integration (Stage 10 skipped). Proxy/user-agent
  settings affect mpv's own HTTP stack only.

## Media coverage

- Startup/memory numbers were measured with synthetic AVI clips (see
  `docs/performance.md`), not a representative codec matrix. Real-world 4K
  HEVC/AV1 files may use more memory/GPU and were not benchmarked here.

## Shutdown path and hang analysis (R1 shipped, R2 not implemented)

- Closing now stops UI timers first, saves settings once, detaches the video
  panel before disposing mpv, stops the statistics refresh while the card is
  hidden (`MainWindow.xaml.cs:2268-2299`), and calls
  `ChromeBlur.SetActive(false)` + `ChromeBlur.Shutdown()` (worker joined for up
  to one second) before the player is disposed, so the 8 Hz screenshot thread
  can no longer touch a disposed mpv handle.
- **Still open**: there is no per-event mpv callback detach step
  (`MpvPlayer.Dispose()` stops its own event thread), and the staging-texture
  flush buffer described in the task does not exist in the code. The report in
  [../reports/hang-analysis-1.2.7.md](../reports/hang-analysis-1.2.7.md)
  recommends these as the next step.
- The two recorded `AppHangB1` events (1.2.5 and 1.2.6) both occurred during
  shutdown while the GPU was in a `DXGI_ERROR_DEVICE_REMOVED` loop; no dumps
  were captured.

## Unsigned downloads

- Neither artifact is code-signed, so SmartScreen and some antivirus heuristics
  will warn. README explains "More info → Run anyway", unblocking the zip/setup,
  and how to compare SHA-256 with the acceptance page. A warning means "unknown
  publisher", not "malware detected".