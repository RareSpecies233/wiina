# Known issues — Wiina v1.3.3

Nothing here is hidden; these are known limitations of the v1.3.3 build. The full decision list (with fix cost and checkboxes) lives in [../reports/known-issues-1.3.1.md](../reports/known-issues-1.3.1.md).

## What changed in 1.3.3

- Opening an OSC or title-bar menu no longer hides the chrome or the cursor:
  every chrome flyout is registered with `TrackChromeFlyout`, the auto-hide
  timer is parked while any popup is open, and the countdown resumes one
  dispatcher tick after the last one closes.
- The mini player is mouse-only: Tab and Space no longer reach its controls.
  This is intentional; there is no keyboard path to mini-player buttons.
- The light-theme mini player uses theme brushes instead of hard-coded dark
  ones (`MiniCoverFallbackBrush`, `MiniCoverScrimBrush`).
- The frame-drop notice moved to the centre of the picture; see below.
- Music and mini mode are reachable only from Settings → Advanced → Modes, and
  neither has a default shortcut.
## What changed in 1.3.2

- Cursor hiding is fixed with an in-process IAT hook (`CursorHider`); see the
  section below for evidence and residual risks.
- Fullscreen no longer shows the 1-3 px top strip, and dragging in fullscreen
  never exits fullscreen.
- OSC/menu polish: media chrome no longer takes keyboard focus, the screenshot
  subtitle switch moved into the Video submenu, auto-hide gained hover
  keep-alive / immediate hide / jitter handling, and the frame-drop notice moved
  to the left edge, vertically centred (ASS `\an4`; moved to the centre in 1.3.3).
- Screenshot names are `name-h-m-s-ms.png`; collisions are numbered.
- Turning thumbnail previews off stops all thumbnail work (no headless mpv, no
  disk writes); the cache-count settings row was removed and the memory cache
  always covers the whole file.
- The uninstaller asks for UAC once at launch and no longer shows an
  "incomplete uninstall" dialog; silent runs stay unelevated.
- Close path measured at 170-179 ms; DXGI device-loss recovery is implemented
  but has not been exercised by a real TDR.

## What changed in 1.3.1


- Key-binding profiles: `Wiina` + `mpv`; the IINA preset was removed and old
  values migrate to Wiina.
- OSC: duplicate Video/Window entries merged, speed presets only in the expanded
  menu, ±5 s previous/next option, immediate hide on pointer leave.
- Thumbnails: optional GPU decode (`video.thumbnail-acceleration`), whole-file
  memory cache (≈41 MB/hour).
- Screenshots: one subtitle switch shared by settings, the quick-settings video
  page and the OSC 更多 -> 视频 submenu.
- Uninstaller: it raises UAC once at launch (re-launching itself elevated),
  removes HKCU and HKLM associations, and only asks whether to keep the config.
  Setup itself stays per-user.

## Settings layout in 1.3.0

The 1.3.0 reshuffle is a layout change, not a behaviour change: row ids and
settings keys are unchanged, so existing `settings.json` files and deep links
keep working. The snapshot in `../reference/settings-layout.md` is the
authoritative map; update it whenever the catalog changes.

## Key binding editor

- One chord per action; modifier+key only, no multi-stroke sequences.
- Conflict handling is Wiina's own (mpv itself is last-wins). The IINA preset
  was removed in 1.3.1; Wiina and mpv remain, and old `IINA` values migrate to
  Wiina. `../reference/iina-shortcuts.md` records the history and deviations.
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
frame does not spam the OSD. In 1.3.2 the notice sat left-middle; since 1.3.3 it
is anchored at the exact centre of the picture with ASS `\an5` and
`\pos(osd-width/2, osd-height/2)`, falling back to 1920×1080 when mpv does not
report a surface size, and lives for 2.2 s. The statistics panel remains the
detailed view.

## Screenshot file names

- Screenshots are named `media-hour-minute-second-millisecond.png`
  (`movie-1-28-39-980.png`); a collision gets ` (2)`, ` (3)`, ... appended.
- Illegal Windows filename characters are replaced, and the media-name part is
  capped at 100 characters.
- With no media loaded (folder picker path) the legacy
  `wiina-yyyyMMdd-HHmmss.png` name is kept.
- The subtitle include/exclude switch is shared by settings, the quick-settings
  video page and the OSC 更多 -> 视频 submenu; the file is always PNG via mpv
  `screenshot-raw` flags `subtitles` or `video`.

## Cursor hiding (fixed in 1.3.2)

Status: fixed and measured on a real 1080p session.

The desktop pointer over a WinUI 3 client area is driven by the input thread
inside Microsoft.UI.Input.dll / Microsoft.InputStateManager.dll /
Windows.UI.Immersive.dll, not by the window procedure or the XAML thread. Those
modules re-apply `SetCursor(arrow)` themselves, so every application-side
`SetCursor`, `ShowCursor`, `WM_SETCURSOR` or `ProtectedCursor` attempt was
overwritten within milliseconds (this is why the 1.3.0 and 1.3.1 attempts were
reported as unverified).

1.3.2 adds `src/Wiina.App/Interop/CursorHider.cs`, which walks the import
address table of a fixed candidate list (`Microsoft.UI.Input.dll`,
`Microsoft.InputStateManager.dll`, `CoreMessaging.dll`, `Microsoft.UI.Content.dll`,
`Microsoft.UI.Xaml.dll`, `Microsoft.UI.Windowing.Core.dll`,
`Windows.UI.Immersive.dll`) and redirects their `user32!SetCursor` entries to a
`UnmanagedCallersOnly` detour that calls `SetCursor(NULL)` while the pointer is
supposed to be hidden. Only IAT pointers are rewritten, never code, and the
original pointer is kept alive so unhide and pass-through still work.

Measured: after hiding, 20/20 `GetCursorInfo` samples returned
`hCursor=0, flags=0` and stayed that way for 4.75 s; after moving the pointer,
54/54 samples returned `flags=1`. `WIINA_CURSOR_HOOK=0` disables the hook,
`WIINA_CURSOR_HOOK=scan` only enumerates importing modules, and a failed
installation keeps the pre-1.3.2 fallback in place.

Residual risks: some antivirus engines treat IAT patching as suspicious
(behaviour is equivalent to a themed cursor, but the technique is unusual), and
a future Windows App SDK could rename the input modules, in which case the hook
finds no import and falls back. Hardware cursors never appear in screenshots,
so `GetCursorInfo` flags are the documented evidence rather than a captured
image.

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
- **Open chrome menus keep the OSC alive.** Every OSC/title-bar menu is tracked
  with `TrackChromeFlyout`: while a popup is open the countdown is parked, and it
  restarts one dispatcher tick after the last popup closes. Nested submenus do
  not multiply the count, and the pointer-outside event raised by the popup
  itself is ignored, so picking a menu item no longer makes the chrome vanish.

## Timeline thumbnails

- Thumbnails are now pre-generated after a file loads and cached on disk
  (`%LOCALAPPDATA%\Wiina\thumbnails\v2`, format v2): one JPEG per 5 s bucket plus
  an atomically rewritten `index.json`; the default budget is 512 MB (Ui →
  Thumbnails → Thumbnail cache, 0 disables it) and eviction is byte-based LRU.
  Hovering reads disk/memory first; misses are queued to the single extraction
  worker and jump ahead of the pre-generation backlog.
- **1.3.1:** thumbnails can decode on the GPU (`video.thumbnail-acceleration`:
  Auto keeps `hwdec=auto-safe`, CPU only forces `hwdec=no`; measured 29 ms wall /
  15.6 ms CPU vs 104 ms / 461 ms per frame), and the **memory** cache now keeps
  every 5-second bucket of the current file (≈57.6 KB per bucket, ≈41 MB/hour).
- **1.3.2:** the cache-count settings row is gone (the memory cache always covers the whole file); turning previews off skips all thumbnail work - no headless mpv instance, no disk writes, no cache sizing.
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

## Mini player

- The mini player is deliberately mouse-only. `MiniPlayerView.SetKeyboardMouseOnly`
  clears `IsTabStop`/`AllowFocusOnInteraction` on every focusable descendant
  (sliders and lazily created playlist rows included) and restores the saved
  values on exit, so Tab cycles the main window instead of landing on mini
  controls. This is a design trade-off, not a regression.
- Light theme uses `MiniCoverFallbackBrush` / `MiniCoverScrimBrush` from
  `Themes/Tokens.xaml` (all three theme dictionaries); High Contrast flattens
  both to system colours.
- Auto-entering the mini player for audio files is disabled. `AutoSwitchToMusicMode`
  is still stored in `settings.json` for compatibility but no longer triggers a
  mode change.

## Mode entry points

- The only entries for music/mini mode are the two action rows on Settings →
  Advanced → Modes (`advanced.music-mode`, `advanced.mini-player`). Both close
  the settings window first, because the mini player takes over the main window.
- There is no default shortcut and the shortcut editor no longer lists these
  actions. Picture-in-picture still has `Ctrl+Shift+P` (shipped Wiina profile),
  and the 窗口 menu label is derived from that profile rather than hard-coded.
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
  under `%LOCALAPPDATA%\Programs\Wiina` (no administrator needed); a machine-wide
  install is not offered.
- The **portable** `Uninstall Wiina.exe` always elevates (`requireAdministrator`
  in `src/Wiina.Uninstall/app.manifest`). Since 1.3.2 the Inno uninstaller of the
  installed copy also raises UAC once at launch: `InitializeUninstall` re-launches
  itself with `runas` and the original instance exits. The old PowerShell
  elevation helper is gone, and no "uninstall was incomplete" dialog is shown.
  Declining UAC still removes the HKCU keys and the app folder; HKLM leftovers
  are written to the log for a later administrator.
- Any uninstaller prompts before force-closing a running Wiina, always removes
  both HKCU and HKLM associations, and only asks whether to keep the
  configuration.
- The uninstaller asks 「是否保留配置文件？」 and defaults to **keep**; a silent
  uninstall also keeps settings. Only an explicit “否” (or
  `--remove-config`) deletes `%LOCALAPPDATA%\Wiina` and `HKCU\Software\Wiina`;
  associations are removed either way.
- The portable `Uninstall Wiina.exe` cannot delete its own running image, so it
  starts a detached batch file that retries for ~30 s. If another process holds
  a file in the folder (antivirus, an open log), deletion may fail and the exe
  reports that the folder must be removed manually (exit code 1).
- The installed copy intentionally does not contain `Uninstall Wiina.exe`;
  use Settings → Apps → Wiina → Uninstall there.

## Fullscreen frame and drag

- The 1-3 px strip at the top of fullscreen was the WinUI XAML island child
  window (`Microsoft.UI.Content.DesktopChildSiteBridge`) sitting at (0,1).
  `WindowInterop.CoverClientArea` covers the client area at startup and after
  presenter changes, so the first pixel row is video.
- Dragging the video area in fullscreen returns immediately, `WM_NCHITTEST`
  answers `HTCLIENT` for the caption band, and the title-bar drag path is
  suspended while fullscreen, so a drag cannot pull the window out of the
  presenter.

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

## Shutdown path and hang analysis (R1 shipped, R2 implemented)

- Closing now stops UI timers first, saves settings once, detaches the video
  panel before disposing mpv, stops the statistics refresh while the card is
  hidden (`MainWindow.xaml.cs` `OnClosed`, 2690+), and calls
  `ChromeBlur.SetActive(false)` + `ChromeBlur.Shutdown()` (worker joined for up
  to one second) before the player is disposed, so the 8 Hz screenshot thread
  can no longer touch a disposed mpv handle.
- **1.3.2:** close path measured at 170-179 ms (process exit 362-426 ms), one settings save, no watchdog. R2 now has `src/Wiina.Core/Playback/DeviceLoss.cs` (classifies `0x887A0005/0x887A0006/0x887A0007/0x887A0020`, bounded `video-reload` recovery, 2 min window / 5 s cooldown). A real TDR has not been reproduced on hardware, so the recovery is code-verified only.
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
