# Wiina release notes

Release date (1.3.8): 2026-10-25

Windows-native, IINA-inspired mpv front end built with WinUI 3, Windows App
SDK 2.5, and libmpv.

## What's new in 1.3.8

### Opening Settings again keeps your place

`App.ShowSettingsWindow` used to default its page argument to `General`, so every
"Settings..." click counted as a deep link and reset an open window. A new
`src/Wiina.Core/Settings/SettingsNavigation.cs` classifies the request as
`ActivateExisting` (no page/row: only bring the window forward, keeping the page,
scroll offset and expanded sections) or `NavigateThenActivate` (explicit page or
row). `App.xaml.cs:548-579` now takes `SettingsPageId? page = null`; the menu and
the welcome window pass no arguments, row-only deep links keep the current page,
and a first open uses the requested page or the last one. Eight
`SettingsNavigationTests` cover the decisions.

### Fullscreen sidebar clicks work again (root cause found)

The 1.3.7 focus guard (`OnRootGotFocus`) pushed focus back to `RootGrid` on the
pointer press, so WinUI cancelled the button `Click` - the sidebar tabs, toggles,
ComboBox popups and sliders were all affected in fullscreen. The guard now returns
early when `control.FocusState == FocusState.Pointer`
(`src/Wiina.App/MainWindow.Playback.cs:2177-2187`); keyboard Tab is still
intercepted. Reproduced and verified with a real mouse, fullscreen, evidence in
`artifacts/v138-bugA-*.png`.

### The PiP placeholder's restore button works

`VideoFrame`'s `OnVideoPointerPressed` is registered with
`handledEventsToo: true`; in PiP it started a drag gesture and stole the pointer
capture, so the restore button never got its `Click`. It now returns early in PiP
(`MainWindow.Playback.cs:2546-2556`). Verified with a real mouse: two windows
collapse to one, the main window returns to 1920x1080 and the video comes back,
evidence in `artifacts/v138-bugB-*.png`.

### Thumbnails: a plan, not a change

`docs/reports/thumbnails-v1.3.8-plan.md` (360 lines) analyses why the slow mode's
three meanings conflict, proposes an effective-mode/disk-cache policy, a keyframe
seek switch (`video.thumbnail-keyframe-seek`, default off), an instant-mode UX
state machine (`ThumbnailOutcome`, no spinner, keep the previous frame) and
breaks down the macOS 15 s vs 30-50 s gap (2.85x frame count, a possible hwdec
fallback, the 20 ms poll floor). Nothing in this release implements it.

### Limits and counts

- Fullscreen and PiP were verified on a single 2560x1325 @ 100% DPI monitor with
  a real mouse; high DPI and multi-monitor were not retested, and the mini
  player's sidebar was not re-run separately.
- Cursor hiding is still unsolved and was not touched in 1.3.8.
- Settings 11 pages / 132 rows / 581 keys; tests 909; artifacts
  `wiina-1.3.8-win-x64.zip` and `Wiina-Setup-1.3.8-x64.exe` (sizes and SHA-256 are
  recorded in the acceptance page after packaging).

## 1.3.7 — previous release

### Fullscreen sidebar input is fixed (mechanism-level)

While the chrome faded, WinUI could re-register the titlebar caption drag
region. In fullscreen the sidebar tab strip sits inside that strip, so clicks
turned into window drags. `ApplyLayout` now calls `SyncFullscreenInputLayers`
(`src/Wiina.App/MainWindow.xaml.cs:1984`, helper at `:2001-2022`) on every
layout pass: fullscreen clears the caption region again and re-asserts
`IsHitTestVisible` on both sidebar hosts, and both hosts carry
`Canvas.ZIndex="2"`. Evidence: `artifacts/v137-fullscreen-sidebar.png`.
The dead zone never reproduced on the development machine, so this is a
mechanism-level fix; if it still happens the next suspect is DPI/island
hit-testing.

### The mini player's restore button is no longer covered

`PlaylistPanel` used `RowSpan=2` without a bottom margin, so an expanded playlist
covered the 72 DIP control row (including restore). It now has
`Margin="0,0,0,72"` (`MiniPlayerView.xaml:185-188`). Only the playlist-expanded
case was affected; PiP regression passed and Tab still stays out of the controls.

### Typed numbers save without pressing Enter

A WinUI `NumberBox` only raises `ValueChanged` on commit, so typing half a value
and closing the window lost it. New `src/Wiina.Core/Settings/NumberInput.cs`
(culture-tolerant parse, clamp, coalesced edit) plus a 200 ms debounce and an
inner `TextBox.TextChanged` hook in the settings window; the pending value is
flushed on focus loss, page change, search, deactivation and close. Verified by
typing 6.5 -> 8.5 into the auto-hide delay and closing without Enter.
The nine spinners in the video-quality editor still use an explicit Apply step.

### Auto playlist follows natural file order

`src/Wiina.Core/Files/PlaylistOrdering.cs` sorts by path segments with numeric
comparison, deduplicates, and returns the index of the file the user opened.
Playback uses the full folder list and reorders the mpv queue with
`playlist-move`, so "EP2, EP10, EP1" now plays in order and starts at the
double-clicked file. Nine new tests plus a folder-expansion test.

### Thumbnail optimisation remains a report

`docs/reports/thumbnails-optimization-1.3.7.md` (333 lines) finds that about
20 ms of each ~50 ms thumbnail bucket is the `Task.Delay(20)` seek-poll floor,
that pushing the downscale into mpv should save roughly 30 %, that keyframe
seeks only suit pre-generation, and that "slow mode" currently means "no
pre-generation". No code changed this round; the P0/P1/P2 route is in the report.

### Counts

Settings 11 pages / 132 rows / 581 keys; tests 901. Cursor hiding is still
unsolved and unchanged in 1.3.7.

## 1.3.6 — previous release

### The blurred chrome now covers the whole chrome band

1.3.5 clipped the blur to the real video rectangle, which left unstretched tint
bands whenever the window aspect did not match the video. 1.3.6 adds
`VideoDisplayRect.ExtendedSize` / `TryMapExtended`
(`src/Wiina.Core/Playback/VideoDisplayRect.cs:137` / `:155`): the blur target
grows to the whole chrome band and the letterbox pixels repeat the nearest video
edge (`ChromeBlur` edge-extend path, `src/Wiina.App/Interop/ChromeBlur.cs:415`,
`:486`, mapping cache `:518-545`, draw branch `:844-880`). Measured on a 490×984
portrait window and a 1384×422 ultra-wide window with no seam in the brightness
profile. Without `osd-dimensions` margins it falls back to the 1.3.5
video-rectangle behaviour.

### "Preview while generating" for timeline thumbnails

New row `video.thumbnail-preview-while-generating` (Settings → Video →
Thumbnails, right after the hover delay; `SettingsCatalog.cs:267-273`), default
**off**, matching IINA's wait-for-`thumbnailsReady` behaviour. Off: while the
background pass is incomplete, hover returns nothing and does not touch the disk
or queue a decode. On: previews appear as buckets are produced. `Instant` mode
always previews and ignores the switch. The gate is
`ThumbnailPolicy.AllowsHoverPreview` (`ThumbnailPolicy.cs:70-77`) plus
`ThumbnailPreGenerationTracker`, applied at `ThumbnailService.cs:478-499`;
background pre-generation requests are not blocked. Applies live
(`MainWindow.Settings.cs:183-189`).

### Picture-in-Picture placeholder follows the theme

`PipPlaceholder` (`MainWindow.xaml:34-60`) now uses `AppBackgroundBrush`,
`PrimaryTextBrush` / `SecondaryTextBrush` and `AccentButtonStyle`, so the
placeholder is readable in light, dark and high-contrast themes.

### A reference for the thumbnail pipeline

New [docs/reference/thumbnails.md](../reference/thumbnails.md) documents both
modes, the extraction chain, the three cache layers, every switch, the new gate,
performance numbers and known limits.

### Counts

Settings 11 pages / 132 rows / 581 keys; tests 876. Cursor hiding is still
unsolved and unchanged in 1.3.6.

## 1.3.5 — previous release

### The chrome blur now follows the real video rectangle

When the window aspect did not match the video, the blurred chrome was sized from
the whole swap chain, so it was stretched and offset. 1.3.5 adds
`src/Wiina.Core/Playback/VideoDisplayRect.cs`: `TryFromOsd` reads mpv's
`osd-dimensions` (`w/h/ml/mt/mr/mb`), converts to DIP with the window's
`RasterizationScale`, sanity-checks the margins (2 px / 2 %) and falls back to a
plain aspect-fit rectangle (`Fit`/`Clamp`) when mpv reports something odd.
`ChromeBlur` now paints only inside that rectangle, so letterbox/pillarbox bars
keep the plain tint and the blur no longer bleeds under the titlebar. Measured on
a 506×993 window with 16:9 content at 100 % DPI: before, the blur covered
`504x992` (`k = 1.575, 5.506`); after, `504x283` at `(0, 354)`, `k ≈ 1.573`.
`WIINA_BLUR_LOG=1` prints `display/fromMpv`. Nine `VideoDisplayRectTests` cover the
conversion and the fallbacks. Limits: 150 % DPI was not tested on real hardware,
panscan/zoom that produce negative margins falls back to the aspect fit, and PiP
was not verified separately.

### Thumbnails no longer contain subtitles

This was a real bug, not a misreading of the preview: the headless extraction
instance also had `sub-visibility=yes` and no `sid`/`sub-auto` override, and
`screenshot-raw` burns subtitles in by default. Now the headless instance uses
`sub-visibility=no`, `sub-auto=no`, `sid=no`, `secondary-sid=no`, `osd-bar=no`,
and `ScreenshotRaw` accepts flags so thumbnails request `"video"` explicitly.
Evidence: the same Hibike file at 600 s showed the subtitle line before the fix;
after it the thumbnail is clean and pixel-identical to `screenshot-raw video`
(mean absolute difference 0.2022).

### The mini player always uses dark chrome

The mini player is an IINA-style dark surface by design: `MiniPlayer.RequestedTheme`
is forced to `Dark`, the root is pinned to `#FF08090A`, and the system backdrop is
suspended while it is open (all restored on exit). Contrast is about 18:1 for
primary text and 9:1 for secondary text, and light- and dark-theme mini screenshots
hash identically. It deliberately does **not** follow the Windows theme; following
it later would need a separate light-token design.

### Instant preview mode for thumbnails

Two new rows: `video.thumbnail-mode` (`Pre-generate (recommended)` / `Instant`) and
`video.thumbnail-hover-delay` (0–2000 ms, step 50, default 250, used only by
Instant). Instant never pre-generates, never reads or writes the disk cache and
does not size the whole-file memory cache; a hover waits for the delay, cancels the
previous hover job and decodes only the current time point. Switching modes applies
immediately, and turning previews off still generates nothing at all. Verified by
trace: `thumbnail mode: instant`, no pre-generation entries, and the disk file
count (3082) unchanged across a session. Limit: the automated check drives the code
path, not a physical pointer.

### Edge snapping no longer jitters

The drag proposal was anchored on the previously snapped rectangle, and the
internal offset accumulated ±2 px per `WM_MOVING`, so a snapped window hopped by
±1–3 px; dragging from the content area also lost the one-pixel follow-up after a
`Move`. `EdgeSnap.Apply` now takes the raw proposed rectangle only, works in integer
physical pixels and snaps at 10 DIP / breaks away at 22 DIP; the main window and PiP
track an anchor plus the cursor delta, skip `Move` when the position did not change,
set DWM state only on transitions, and a 16 ms drag pump keeps tracking when the
pointer capture is lost. Verified with `t62-content-rel4.csv`: sitting on the edge
shows a visible gap of 0, 35 static frames stay constant, and moving 35 px back
breaks away at 23 px of visible movement. Limits: measured at 100 % DPI on a single
monitor; on a very slow machine the 16 ms pump can lag by one or two frames.

## 1.3.4 — previous release

### An unfocused window no longer wakes the OSC

`OnRootPointerEntered`, `OnRootPointerMoved` and `ShowChrome` now return early
while the window is inactive and the chrome is hidden (`!_windowActive &&
!_chromeShown`), so hovering an unfocused window no longer pops the OSC back.
Losing focus still hides the chrome through the existing `Activated` path
(docked layout and an open sidebar remain exempt). Measured with a real mouse:
hover on an unfocused window does nothing; after clicking to activate, moving
the pointer shows the OSC.

### The reported "mini window" was Picture-in-Picture

The keyboard and light-theme defects the user saw were in `PipWindow`, not in
`MiniPlayer`. Picture-in-Picture no longer lets Tab land on its transport
buttons, and its root `PreviewKeyDown` swallows Tab, maps Space to play/pause,
Enter to fullscreen (leaving PiP first) and Escape to restore the main window.
Every other key is forwarded to the main window, and a key only counts as
handled when the main window actually ran a binding. Verified with real keys:
Left/Right seek, Up/Down volume, M mute, Ctrl+S screenshot, Enter/Escape,
Tab x5 stays on the pane.

### Light theme for PiP and the mini player

The PiP root background is a `ThemeResource AppBackgroundBrush` instead of a
hard-coded black; the mini player applies an opaque theme brush plus
`MicaBackdrop` on entry and restores the previous background on exit.
Screenshots: `artifacts/pip-light.png`, `mini-light-mini.png`,
`mini-dark-mini.png`.

**Known limit:** the outermost 8 px of every window is DWM's invisible resize
border - it shows the desktop wallpaper and cannot be painted by the app. DPI
above 100 % and High Contrast were not re-tested this round.

### The dropped-frame notice is a native card

The transient dropped-frame notice is now a WinUI `Border` in the overlay
layer (`MainWindow.xaml`), centred on the picture, non-hit-testable, shown for
2.2 s and faded over 180 ms. The mpv `show-text`/ASS `\an`/`\pos` path was
deleted; the text comes from `FrameDropNotice.Describe()` and a unit test
asserts that no ASS markup creeps back in.

### One mini-player entry, behind a development warning

Settings -> Advanced now has a single row, "Mini player (music mode)"; the
separate `advanced.music-mode` row is gone. The description warns that the
feature is still in development with limited stability, and a `ContentDialog`
confirmation (default: cancel) appears before the settings window closes.

### Cursor hiding is still not solved

Nothing in 1.3.4 changes the cursor path. The 1.3.2 IAT hook remains the last
attempt and is still overridden by the WinUI input stack in some
configurations; treat it as unverified, not fixed.

### Counts

Eleven pages, 129 settings rows, 571 UI strings, 833 tests.

## 1.3.3 — previous release

### Opening a chrome menu no longer hides the OSC

Every menu that belongs to the player chrome is now registered with
`TrackChromeFlyout` (`MainWindow.Menu.cs` for the OSC "more" menu,
`MainWindow.xaml.cs` for the custom title-bar window menu). `Opened` stops the
auto-hide timer and increments a popup counter; `Closed` decrements it and, only
when the count reaches zero, restarts the countdown one dispatcher tick later.
Nested submenus share the same popup and therefore keep the counter at 1. A
pointer-exit raised by the menu's own popup window no longer hides the chrome
immediately. The cursor follows the chrome as before.

### The mini player ignores Tab and Space

`MiniPlayerView.SetKeyboardMouseOnly` walks every focusable descendant (slider
and lazily created playlist rows included), records its original
`IsTabStop`/`AllowFocusOnInteraction`, and turns both off while the mini player
is open; leaving the mini player restores them. Pressing Tab three times and
then Space keeps playback paused, and mouse clicks on mini-player buttons still
work. Mini-player controls being unreachable from the keyboard is a deliberate
trade-off.

### The light theme mini player is no longer black

`Themes/Tokens.xaml` defines `MiniCoverFallbackBrush` and `MiniCoverScrimBrush`
for all three theme dictionaries, and the fourteen hard-coded dark brushes in
`MiniPlayerView.xaml` now use `ThemeResource`. Light renders light grey/white,
Dark keeps the dark gradient, and High Contrast uses a flat system colour.

### The frame-drop notice sits in the centre of the picture

The transient notice now uses `\an5\pos(osdWidth/2, osdHeight/2)`, falling back
to 1920×1080 when `osd-width`/`osd-height` are unavailable, and disappears after
2.2 s. It is no longer covered by a visible title bar, and the statistics card
keeps its own dropped-frame rows.

### Mini and music mode now live in Settings only

The 窗口 menu keeps picture-in-picture (`video.pip`, labelled `Ctrl+Shift+P`
from the real profile) but the `video.music-mode` and mini-player entries are
gone. Audio files no longer switch to the mini player automatically
(`AutoSwitchToMusicMode` is kept only as a stored value). The single entry point
is Settings → Advanced → Modes (`advanced.music-mode`,
`advanced.mini-player`); both close the settings window before switching, have
no default shortcut, and no longer appear in the shortcut editor. The PiP menu
label is now derived from the shipped Wiina profile, and the profile test suite
asserts that PiP has a chord while music mode has no action and no chord.

### Counts

Eleven settings pages, 130 rows, 569 UI strings, 830 tests. Artifacts:
`artifacts/wiina-1.3.3-win-x64.zip` and
`artifacts/Wiina-Setup-1.3.3-x64.exe` (sizes and SHA-256 recorded in
`docs/acceptance/v1.3.3.md` after packaging).

## 1.3.2 — previous release

### Cursor hiding actually works now

The pointer stayed visible because the WinUI input stack re-applies
`SetCursor(arrow)` on its own input thread; `SetCursor`, `ShowCursor`,
`WM_SETCURSOR`, and `ProtectedCursor` from application code never reached that
thread. `src/Wiina.App/Interop/CursorHider.cs` now patches the import address
table of the candidate modules (`Microsoft.UI.Input.dll`,
`Microsoft.InputStateManager.dll`, `CoreMessaging.dll`, `Microsoft.UI.Content.dll`,
`Microsoft.UI.Xaml.dll`, `Microsoft.UI.Windowing.Core.dll`,
`Windows.UI.Immersive.dll`) so their `user32!SetCursor` calls become
`SetCursor(NULL)` while chrome is hidden. Only IAT pointers change; the original
pointer is kept alive for pass-through and restore. Measured: 20/20
`GetCursorInfo` samples reported `hCursor=0, flags=0` for 4.75 s after hiding,
and 54/54 reported `flags=1` after the pointer moved. `WIINA_CURSOR_HOOK=0`
disables the hook; `=scan` only lists importing modules. If installation fails
the old fallback stays in place. Risk: some antivirus engines flag IAT patching,
and a future Windows App SDK may rename the modules — both cases fall back.

### Fullscreen edge and drag fixes

- The 1-3 px strip at the top of fullscreen came from the WinUI XAML island
  child window (`Microsoft.UI.Content.DesktopChildSiteBridge`) sitting at (0,1).
  `WindowInterop.CoverClientArea` now covers the client area at startup and after
  presenter changes; the first pixel row is video.
- Dragging the video in fullscreen no longer drops fullscreen: the gesture
  returns early, `WM_NCHITTEST` reports `HTCLIENT` for the caption area, and the
  title-bar drag path is suspended while fullscreen.

### OSC and menus

- Media-chrome controls (OSC, titlebar, mini player) are removed from the Tab
  order and from click-focus, so Space/Enter keep their player meaning; Tab in
  mini/fullscreen is returned to the root grid.
- Fixed eight menu groups (File, Playback, Video, Audio, Subtitles, Plugins,
  Window, Help). The Video menu keeps picture-only commands; window controls
  live only in Window. The screenshot-subtitles toggle moved from the expanded
  flyout top level into Video. Menu items went from 83 to 80 and
  `MenuRulesTests` (13 cases) covers the layout.
- Auto-hide hardening: 5x5 DIP jitter threshold, hover keeps the OSC parked,
  leaving the window or deactivating hides immediately, clicking the video hides
  according to Control > Mouse > click action, the cursor follows the chrome, and
  fast enter/leave no longer leaves the timer in a wrong state.
- Dragging the timeline and all seek shortcuts show the progress OSD
  (`OsdMessages.Seek`), and the frame-drop notice is pinned to the left edge,
  vertically centred, instead of the top-centre slot.
- The uninstaller raises UAC once at launch (Inno `InitializeUninstall`
  re-launches itself elevated and exits the old instance; the PowerShell
  elevation helper is gone). The "uninstall was incomplete" dialog is removed —
  machine-wide leftovers are logged only. Silent uninstalls stay unelevated.

### Screenshots and thumbnails

- Screenshot names are now `media-hour-minute-second-millisecond.png`
  (`movie-1-28-39-980.png`); collisions get a numeric suffix and illegal
  characters are sanitised. Without media the legacy
  `wiina-yyyyMMdd-HHmmss.png` name is kept.
- The thumbnail cache-count setting row is gone. Turning previews off now stops
  all thumbnail work: no headless mpv instance and no disk writes. The memory
  cache sizes itself to every 5-second bucket of the current file
  (about 57.6 KB per bucket).

### Shutdown and device loss

Close takes 170-179 ms (process exit 362-426 ms) with a single settings save and
no watchdog. `src/Wiina.Core/Playback/DeviceLoss.cs` classifies
`0x887A0005/0x887A0006/0x887A0007/0x887A0020` and schedules a bounded
`video-reload` recovery; a real TDR has not been reproduced on hardware, so the
recovery path is code-verified only.

### Known issues and documentation

`docs/reports/known-issues-1.3.1.md` now carries the user's 1.3.2 decision for
every entry (feature / expected / fixed). New `docs/acceptance/v1.3.2.md`
records the eleven user items, the decision table, the settings delta
(129 -> 128 rows, 563 -> 568 keys, still eleven pages) and 827 tests.

## 1.3.1 — previous release

### Key-binding profiles: Wiina and mpv only

The approximate IINA preset is gone. `KeyBindingProfiles.All` now exposes
`Wiina` (79 chords generated from `wiina-defaults.conf`, including `Ctrl+,` for
settings) and `mpv` (18 chords); "mpv" no longer carries a "默认/default"
suffix. Persisted `IINA`/`IINA 默认` values migrate to Wiina on load, and custom
profiles are untouched.

### OSC menus are de-duplicated

- Duplicate Video/Window entries collapse into the Window menu: screenshot,
  deband, filters and quality moved there, and the Video menu no longer repeats
  them. Accelerator text is resolved from the active profile, so it matches the
  keys that actually fire.
- Speed presets live only in `⋯ → 置顶 → 速度 ▸` (0.5/0.75/1/1.25/1.5/2/2.5/3/4×);
  the quick-settings home page no longer duplicates them and the player page
  keeps its slider. The speed label appears on the right side only.
- Previous/next can act as ±5 s (Ctrl = ±30 s) through `ui.osc-prev-next`.
- Moving the pointer out of the window now hides the OSC immediately instead
  of waiting for the idle delay; an open flyout or a held mouse button defers
  the hide until the popup closes.
- The transient dropped-frame notice is anchored to the left-middle of the
  picture via ASS tags, so it no longer sits under the statistics panel.

### Thumbnails can use the GPU, and the memory cache covers the whole file

- New "缩略图加速" setting: Auto (recommended) keeps libmpv's
  `hwdec=auto-safe`; CPU only forces `hwdec=no`. Measured per frame: 29 ms wall
  / 15.6 ms CPU (auto) vs 104 ms / 461 ms (CPU only).
- The in-memory cache now keeps every 5-second bucket of the current file: about
  57.6 KB per bucket, ≈41 MB/hour (24 min ≈16 MB, 2 h ≈83 MB). The disk budget
  (`ui.thumbnail-cache-mb`) still applies.

### Screenshots can include subtitles

One persisted switch in Settings → Video → 截图 and the expanded OSC menu
(default on; there is no quick-settings entry for it). The value is applied through mpv's
`subtitles`/`video` screenshot flags.

### The uninstaller always removes associations; the portable one always elevates

The portable uninstaller manifest is now `requireAdministrator`: it always asks
for elevation, offers to force-close a running Wiina, removes both HKCU and HKLM
associations, and only asks whether to keep the configuration. The installer
itself stays per-user (`PrivilegesRequired=lowest`), so installing never needs
administrator rights; the installed copy's Inno uninstaller therefore starts
unelevated and asks for UAC only when machine-wide associations still exist.
Both uninstallers remove file associations unconditionally, and the prompt only
covers keeping the configuration.

### Cursor hiding: implemented, not confirmed

A transparent cursor resource (id 101), `ProtectedCursor` on all 489 elements
under `RootGrid`, and a 60 ms helper-thread `SetCursor(NULL)` all build and pass
tests, but no stable hidden pointer was observed — CoreMessaging re-applies the
arrow within milliseconds. This is documented as partial, not as fixed; see
`../reports/known-issues-1.3.1.md` (K26).

### A known-issues list you can triage

`../reports/known-issues-1.3.1.md` collects 30 issues (P0/P1/P2) with symptoms,
causes, workarounds, rough fix cost, involved files and a checkbox per item.

780 tests pass (Core).

## 1.3.0 — previous release

### The settings window got a layout pass

Eleven pages, 126 rows: the utilities page is folded into Advanced (diagnostics,
tools, folders), the six thumbnail rows moved to Video → 缩略图, the screenshot
folder moved to Video → 截图, "open URL" moved to General → 播放与历史, and the
new "system media keys" toggle lives on Controls. Row ids are unchanged, so
`settings-layout.md` deep links and quick-settings still work.

### Key binding editor rebuilt

The editor is now searchable and category-grouped, with a read-only built-in
list for Wiina, mpv and IINA profiles, custom profiles (new/rename/delete),
chord recording with explicit conflict handling (replace or cancel), buffered
edits behind an explicit Save plus an unsaved-close confirmation, JSON
import/export and a per-profile reset. The default profile stays Wiina.

### Portable mpv.net configuration export

`resources/mpvnet/` now carries a configuration for mpv.net users:
`wiina.conf` (mpv options plus a `[wiina]` profile, loaded with
`profile=wiina`), `wiina-input.conf` (the Wiina key bindings), and a README
explaining where each file goes. The same bindings are embedded in Wiina as
`src/Wiina.Core/Input/wiina-defaults.conf`. This is an interop export, not a
supported second player: Wiina's side panels, composition rendering and custom
UI do not exist in mpv.net, and mpv.net/stock mpv cannot load Wiina's settings.

### System media keys (SMTC)

A new Controls → 键盘 → 系统媒体键 toggle registers Wiina with the Windows
System Media Transport Controls, so play/pause/next/previous and the
media-key overlay work while Wiina is running. The setting applies immediately.

### Frame-drop detection restored

A 3-second sliding window with a 12-second cooldown shows a transient
`Wiina · 丢帧 N 帧/分（总计 M）` OSD when frames start dropping, independently of
the statistics panel (which used to own that switch). The statistics panel also
gained 丢帧 / 解码丢帧 / 丢帧速率 rows and falls back from `frame-drop-count` to
`vo-drop-frame-count` when the composition renderer leaves the former empty.

### Timeline thumbnails: slow mode

Video → 缩略图 → 慢速预览图模式（单核） runs the thumbnail worker on one
low-priority thread and skips whole-file pre-generation; the disk cache is
still used, only the first hover of a bucket is slower. Width, bucket and cache
size rows now sit together on the Video page.

### Cursor hiding is still limited in WinUI 3

The intent was for the pointer to disappear together with the chrome, but this
release does not achieve it. Investigation proved that the desktop cursor over a
WinUI 3 client area is owned by CoreMessaging: `WM_SETCURSOR` overrides,
thread-level `SetCursor(blank)`, and a reflected `UIElement.ProtectedCursor =
null` all leave the arrow visible (`GetCursorInfo` still reports 0x10019 and
`InputSystemCursorShape` has no "none" value). The root cause, the three inert
mechanisms, and the next route (embedded transparent `.cur` +
`InputDesktopResourceCursor`) are documented in known-issues. Chrome hiding,
click-to-hide, focus-loss hiding and the auto-hide delay itself are unaffected.

### Crash log path

The crash dialog's "collect logs" buttons now do what they say: one opens the
log folder, the other copies the log path. Logs stay under
`%LOCALAPPDATA%\Wiina\logs` and are never uploaded anywhere.

### No-border fullscreen

Fullscreen asks DWM to drop the window-corner rounding and the border colour,
so the video reaches the screen edge; the style is restored on exit and
re-applied after presenter or DPI changes. Windows 10 ignores the attributes.

### Installer upgrade behaviour

The per-user installer upgrades in place without a restart prompt and keeps
`%LOCALAPPDATA%\Wiina` and the HKCU file associations. Only an explicit
"do not keep settings" answer in the uninstaller removes them.

### Stability notes

The 1.2.7 close-path fix (`ChromeBlur.Shutdown`, called from `OnClosed` before
the mpv player is destroyed) is carried forward. DXGI device-lost recovery is
**not** implemented; the freeze analysis remains a report, not a fix list.

### Documentation

`docs/reference/settings-layout.md` is refreshed for the 11-page layout,
`docs/reference/iina-shortcuts.md` compares the Wiina/mpv/IINA defaults,
`resources/mpvnet/README.md` explains the export, and the
test suite is at 761 tests.

## 1.2.7 — previous release

### Timeline thumbnails now match IINA (pre-generated + disk cache)

- After a file loads, the whole timeline is pre-generated once in the background
  (one frame per 5-second bucket, 90 px tall) and stored under
  `%LOCALAPPDATA%\Wiina\thumbnails\v2`: one JPEG per bucket plus an atomically
  rewritten `index.json`, byte-budgeted LRU eviction (512 MB default) and a
  format-version directory so older layouts are discarded. Hovering reads
  disk/memory first; misses go to the single extraction worker and jump ahead of
  the pre-generation backlog.
- New settings: Ui → Thumbnails → Thumbnail cache (0–4096 MB, 64 MB steps,
  default 512; 0 disables disk caching) and Clear thumbnail cache (reports the
  freed size). Network/UNC sources stay memory-only.
- Measured on a 1080p 23:40 HEVC file: cold pre-generation of 285 buckets took
  14.1–14.9 s, warm start 0.3 s; hover 73 ms cold / 4 ms warm; 1.70 MB on disk
  for 285 frames.

### Floating OSC follows the sidebar animation (cursor hiding is still limited)

- A sidebar that is already animating out no longer reports its full width, and
  the floating OSC is re-positioned as the hide animation starts, so it no longer
  waits for the panel and then snaps back.
- Cursor hiding was attempted with `WM_SETCURSOR`, thread-level `SetCursor` and
  `ProtectedCursor`, but WinUI 3's CoreMessaging keeps the arrow visible. The
  limitation, the three inert mechanisms and the next resource-based route are
  in known-issues; this item is not claimed as fixed.

### 0.5× / 1× / 2× use the real video resolution

- `WindowSizing.ScaledVideoWindowSize` maps mpv's display size (rotation and
  pixel aspect included) to 1:1 physical pixels, converts to DIP, keeps the
  display aspect ratio and clamps to the work area; 1280×720 is only the no-video
  fallback. Fit to screen picks the largest window with the video's aspect.

### Fullscreen has no leftover border

- DWM corner preference and border colour are set through one `SetWindowFrame`
  call with a fullscreen > snapped > default priority; fullscreen always clears
  the border and rounding, and the frame is re-applied when the presenter
  changes. Ignored on Windows 10 (no-op).

### Machine-wide file associations (admin mode)

- Settings → File associations gains "Associate as administrator…": it relaunches
  `Wiina.exe --associate-elevated-video --associate-elevated-audio` through UAC,
  writes `HKLM\Software\Classes` ProgIDs/Capabilities/RegisteredApplications and
  best-effort removes the per-extension `UserChoice`. Unregister removes Wiina's
  own keys from both HKCU and HKLM. The per-user path stays; Windows still needs
  the Default Apps confirmation.

### Unsigned-download guidance

- README now explains SmartScreen and antivirus false positives and how to
  verify SHA-256.

### Shutdown path and hang analysis

- Close now stops every UI timer first, saves settings once, detaches the video
  panel before disposing mpv, and stops refreshing the statistics card while it
  is hidden. See `docs/reports/hang-analysis-1.2.7.md`; the report's
  `ChromeBlur`-stop / handler-detach items are not implemented and are recorded
  as such in the acceptance page.

### Performance comparison

- All 23 quality options apply live; only `vo` / `gpu-api` / `gpu-context` /
  `d3d11-output-mode` are fixed at startup. See
  `docs/reports/performance-vs-potplayer.md`.

### Documentation

- New acceptance page, hang analysis and PotPlayer comparison; README, known
  issues, performance, accessibility, PLAN, installer, file associations and
  reference pages refreshed. 711 tests pass (maintainer run; this docs pass did
  not build).

## 1.2.6 — previous release

### The OSC moves only when a panel would cover it

- Opening quick settings, the playlist, chapters or the plugins panel used to
  re-place the floating OSC unconditionally, even when the panel did not overlap
  it. The floating OSC is now clamped horizontally only when its rectangle
  intersects the visible sidebar (8 DIP gap, 0.5 DIP vertical slack), so the
  controls stay put when the panel sits above or beside them.
- Dragging the floating OSC and restoring the saved position use the same rule.
  The docked layout is unchanged: the bottom bar belongs to the layout and still
  insets by the panel width.

### Auto-hide is driven by an explicit idle clock

- New setting **Settings → 界面 → 窗口与 OSC → 自动隐藏延时**
  (`ui.osc-autohide-seconds`, 0.5–30 s, default 2.5). It clamps on both the
  settings model and the state machine.
- Pointer activity is timestamped on a monotonic `Stopwatch`. Moves smaller than
  **5 DIP on both axes** are ignored: they neither show the chrome nor reset the
  idle countdown, so a hand resting on the mouse cannot keep the OSC alive.
- The idle clock — not the timer tick — decides the fade. A resting pointer over
  the bottom bar in fullscreen now hides on schedule, which fixes the
  fullscreen "OSC never disappears" bug.
- Losing focus hides immediately unless the layout is Docked or a sidebar is
  open. A left click on the video hides visible chrome only when the press and
  release did not travel; Docked layout never auto-hides; PiP and mini ignore
  the player chrome entirely.

### Audio output device is a real dropdown

- Settings → 音频 → 音量与输出 now lists the devices libmpv reports
  (`audio-device-list`): "跟随系统默认" first, then human-readable descriptions
  with the raw mpv name in the tooltip (duplicated descriptions are
  disambiguated with the name), plus a refresh button.
- A saved device mpv does not currently report stays selectable as
  "不可用（<name>）" instead of being silently rewritten. Switching applies
  immediately; before a player exists the dropdown offers only the system
  default.
- No polling: the refresh button re-reads the main window's cached list, which
  mpv updates when the player starts or the device set changes.

### Settings and reference docs

- Settings stays at 12 pages; the new auto-hide delay row brings it to **122
  rows**, and no existing row id changed.
- New: `docs/reference/settings-layout.md` (full 12-page / 122-row snapshot) and
  `docs/reference/iina-thumbnails.md` (how IINA pre-generates and caches
  timeline thumbnails vs live per-hover extraction at the time; alignment was
  **planned, not implemented**).
- 668 tests pass (maintainer run; this docs pass did not build or re-run).

## 1.2.5 — previous release

### Theme changes apply everywhere, immediately

- Changing the theme from **Settings → 通用 → 主题**, the quick-settings menu or
  any other window now repaints **every open window**: the player titlebar and
  floating OSC, the sidebar, Settings, quick settings, PiP, about, log viewer,
  inspector, filters, key bindings, welcome and the quality editor.
- **ThemeMode.System follows the Windows app theme live.** WinUI freezes
  `Application.RequestedTheme` at launch and `ElementTheme.Default` does not
  re-resolve, so the app resolves a concrete dark/light value from
  `UISettings.GetColorValue(UIColorType.Background)` and listens to
  `ColorValuesChanged`.
- Both directions work: switching in Settings updates the playing window and
  its floating OSC; switching in the sidebar quick menu updates an open Settings
  window. Theme rows show the new value in both places.
- Limitation: the window material (`DesktopAcrylicBackdrop`) can lag one paint
  behind the content on some systems, and light/dark switching under High
  contrast was not visually verified.

### Opaque Solid mode is actually opaque

- Fixed the regression where switching from a blur mode to
  **关闭毛玻璃（纯色）** could still show video texture. Chrome panels no longer
  assign `Background` from code: the opaque brush stays declared in XAML and the
  blur/tint layers are child elements toggled by `Visibility`. Switching
  Dynamic → Solid → Fixed → Dynamic restores glass without a hide/show cycle.
- The dark Solid base is now a dark gray **`#202227`** (was near-black
  `#15171B`), and the dark blur tints were aligned to the same gray.
- The **OSD card joins the blur pipeline**: it follows the global four-mode
  setting (glass in 固定/动态/始终, opaque in Solid) through the same
  `ChromeBlur` service, and still never takes input.

### Quality manager: 23 mpv options, IINA-aligned defaults

- The quality editor now exposes **23 video-quality options** in seven groups —
  缩放, 色度, 抖动, 去色带, 插帧, 同步, 其他 — each with a Chinese label, a
  one-line trade-off hint and its raw mpv key. The window scrolls; the 9 numeric
  fields are compact `NumberBox` spin controls (a 1.2.5 fix — the spinner column
  was missing, so the buttons overlapped the label).
- **Default preset = mpv 0.41 defaults, which IINA does not override**
  (`MPVOptionDefaults.swift` in the local IINA checkout): `scale=lanczos`,
  `dscale=hermite`, `cscale=bilinear`, `dither=fruit`, `dither-depth=auto`,
  `dither-size-fruit=6`, all `*-antiring=0`, `correct-downscaling=yes`,
  `linear-downscaling=yes`, `sigmoid-upscaling=yes`, `temporal-dither=no`,
  `interpolation=no`, `tscale=oversample`, `video-sync=audio`,
  `video-sync-max-video-change=1.0`, `scaler-resizes-only=yes`,
  `gpu-dumb-mode=auto`, `deband-iterations=1`, `deband-threshold=48`,
  `deband-range=16`, `deband-grain=32`.
- **性能优先** = `bilinear` on scale/dscale/cscale, `dither=no`, all three
  quality checkboxes off, deband off, interpolation off — the cheapest coherent
  set.
- **去色带** is now a master switch in **Settings → 视频 → 画面** (`video.deband`,
  default off). The profile keeps its four deband parameters for JSON
  round-trip, but they only reach mpv while the switch is on.
- Only two built-ins remain: **默认** and **性能优先**. `自动（IINA 默认）`,
  `均衡`, `高质量` and the `Balanced` / `HighQuality` enum values normalize to
  默认 on JSON import; unknown imported keys are preserved and shown as
  “未识别（保留原值）”.

### Other fixes

- The Statistics card follows the OSD placement and uses theme-correct text in
  dark mode; it stays the only statistics surface (the persistent technical OSD
  was removed in 1.2.4).
- 659 Core tests pass (maintainer release run).
- Portability: the portable zip is `wiina-1.2.5-win-x64.zip`; the per-user
  installer is `Wiina-Setup-1.2.5-x64.exe`.

## 1.2.4 — previous release

### What's new in 1.2.4

### Chrome blur is back — as four modes

- **Settings → 界面 → 窗口与 OSC → 毛玻璃模式** offers four values:
  - **固定（静态帧 / Fixed）** — capture one frame when the chrome becomes
    visible, then reuse it until it hides.
  - **动态（跟随视频 / Dynamic, default）** — capture while the OSC/OSD/sidebar
    chrome is visible and follow the video.
  - **始终（持续计算 / Always）** — keep calculating even while no chrome is
    visible.
  - **关闭毛玻璃（纯色 / Solid）** — no capture and no blur at all.
- The three glass modes reuse `src/Wiina.App/Interop/ChromeBlur.cs`: one
  `screenshot-raw` frame from libmpv every 125 ms, box-downscaled to 320 px,
  blurred by Win2D `GaussianBlurEffect` (`BlurAmount = 9`) on a worker thread,
  and published to the titlebar, docked/floating OSC and sidebar.
- **Solid costs nothing.** It never creates the Win2D device or the capture
  thread; the surfaces paint the opaque `ChromeSolidBrush` mixed 16 % toward
  the system accent (`Services/ChromeTint.cs`), which follows
  `UISettings.ColorValuesChanged`. `WIINA_BLUR_DISABLE=1` forces Solid for
  support and A/B comparison.
- (1.2.4 behaviour; changed in 1.2.5 — the OSD card now follows the global mode) OSD message text stays opaque in every mode, and in the Docked layout the
  titlebar stays opaque because the video lives below the bar, not behind it.

### Statistics card: OSD placement, theme-correct text, only stats surface

- The native WinUI statistics card now follows the **OSD placement** setting
  (the same six anchors as OSD messages) and uses theme-correct text in dark
  mode (no more black-on-black).
- The **persistent technical OSD feature was removed entirely**:
  `AppSettings.OsdPersistent`, the `ui.osd-persistent` and
  `utilities.technical-osd` catalog rows and the Quick Settings toggle are
  gone. The card is the only statistics surface; it still refreshes once per
  second and groups rows under 文件 / 视频 / 音频 / 播放 / 渲染 (29 rows).

### Quality presets: two built-ins, deband moved to the Video page

- Exactly two built-in presets remain: **默认** (`ewa_lanczos` / `mitchell` /
  `bilinear`, the IINA/libmpv combination) and **性能优先** (all three scalers
  `bilinear`).
- The 1.2.3 **均衡 / 高质量** presets were deleted; legacy enum names still
  deserialize and are normalized to 默认, both in `settings.json` and in
  imported preset JSON.
- **去色带** left the preset editor. It is now Settings → 视频 → 画面 → 去色带,
  default **off**, and toggling it applies `deband=yes/no` to the running
  player immediately. `VideoQualityProfile.Deband` is kept only so exported
  profile JSON keeps round-tripping.

### Snapping: flush on all four edges, every drag path, breakaway hysteresis

- New `src/Wiina.Core/State/FrameMargins.cs` and
  `WindowInterop.TryGetFrameMargins` convert between the outer window rectangle
  and the **visible DWM frame** (`DWMWA_EXTENDED_FRAME_BOUNDS`), so a "snapped"
  window really touches the monitor edge instead of leaving the invisible
  7–8 px resize border as a gap.
- 10 DIP snap distance / 22 DIP breakaway hysteresis, scaled per monitor DPI;
  the breakaway distance is deliberately larger than the snap distance so a
  snapped window can be pulled off again.
- Works on the native titlebar (`WM_MOVING`), the video-area drag, the
  mini-player drag and the PiP drag; the mini player now snaps too.
- While snapped, the DWM border line and rounded corners are suppressed, so no
  white seam appears; both are restored on breakaway. Position-only: size,
  aspect lock and minimum sizes are untouched.

### Mini player / PiP: full-resolution picture, simplified UI only

- The 640×360 compositor-upscale cap is gone: `PipGeometry.RenderSize` returns
  the full window pixel size, and `MpvVideoView.SetLayoutSize` keeps the XAML
  surface at the full window size. Growing a PiP window now scales the picture
  instead of only enlarging the frame.
- Cost is saved only by the simplified UI (no acrylic/shadow over the video),
  never by rendering into a low-resolution buffer.

### Settings rail and settings tab audit

- Rail items at rest and on hover are **pixel-identical** (dim
  `SecondaryTextBrush`, no fill, no border); only the selected item is tinted,
  and keyboard focus is a 2 px accent ring. The icon and label now bind their
  `Foreground` to the button, so rest/hover/selected stay in lockstep.
- Tab audit (row ids unchanged): edge snapping moved to 界面; quality preset,
  preset editor and deband live on 视频 → 画面; diagnostics and folders were
  consolidated on 高级; `utilities.open-url` moved to 高级; 工具 keeps only
  字体 / 日志查看器 / 检查器 / 关于; 插件 is an honest Stage 9 placeholder;
  按键绑定 stays a dedicated window (no rail entry, redirect row on 控制).

### Installer, uninstaller, and portable zip

- **New per-user Inno Setup installer** `Wiina-Setup-1.2.4-x64.exe`
  (`installer/wiina.iss`, `PrivilegesRequired=lowest`, default
  `%LOCALAPPDATA%\Programs\Wiina`), with the official Chinese Simplified
  translation (`installer/ChineseSimplified.isl`) plus English.
- **Uninstall asks 「是否保留配置文件？」**, default **Yes**: settings, logs and
  file associations stay. Answering **No** deletes `%LOCALAPPDATA%\Wiina`, the
  `Wiina.Video`/`Wiina.Audio` classes and their `OpenWithProgids` values,
  `RegisteredApplications\Wiina` and `HKCU\Software\Wiina` — user media is
  never touched. Silent uninstall keeps settings.
- **The portable zip now ships `Uninstall Wiina.exe`** (single-file trimmed
  `src/Wiina.Uninstall`). Interactive use asks for confirmation, whether to
  keep settings (default Yes) and whether to remove the file associations;
  switches: `--silent`, `--keep-config` / `--remove-config`,
  `--remove-associations` / `--keep-associations`, `--no-self-delete`,
  `--dry-run`. It schedules its own folder deletion through a detached batch
  file that retries for ~30 s. The installed copy excludes this exe because
  Inno owns the uninstaller there.

### WinUI 3 fixes and cleanup

- Blur layers re-attach when the mode changes, are effect-only (no hit
  testing), and fall back to the solid brush when capture fails.
- The statistics card follows the root `ActualTheme` at runtime.
- Settings rail foreground binding (above) replaces the theme fallback colour
  that read as a hover highlight.

### Documentation

- README, release notes, known issues and the architecture records were
  re-audited against the code. ADR-0003 is current again (blur pipeline and
  the four modes); ADR-0004 now documents Solid mode as one option instead of
  a removal. A new [installer/uninstall guide](../installer.md) records the
  per-user install, the keep-settings prompt and the portable uninstaller.
  The [1.2.4 acceptance record](../acceptance/v1.2.4.md) lists the ten issues
  of this round.

### Changes since 1.2.3 worth knowing

- The 1.2.3 "solid chrome only" design is history: the blur pipeline, Win2D
  package and `ui.blur-mode` row are back. The setting is `ChromeBlurMode`
  again; a 1.2.3 `settings.json` that stored `"ChromeBlurMode": "Solid"`
  stays Solid until changed.
- Persistent technical OSD is gone for good; the statistics card replaces it.
- PiP sizes stored by 1.2.3 are still valid; the render cap that used to clamp
  them no longer exists, so a restored PiP window renders at full resolution.
- 1.2.3 remains in git history (PR #2) and its acceptance record is kept as a
  historical document.

### Intentionally not included

- **Stage 9 (plugins) is still skipped.** No plugin host, plugin API,
  `Info.json` parser, or WebView2 dependency. See
  [../plugins/README.md](../plugins/README.md).
- **Stage 10 (online subtitles / yt-dlp) is still skipped.** No OpenSubtitles
  client, no yt-dlp integration, no bundled yt-dlp. Menu/settings entries
  remain disabled and say so.
- **No silent default-handler takeover.** By Windows design, associating file
  types can only make Wiina an eligible handler; confirmation happens in
  Default Apps.
- **No MSIX / Store package and no auto-update.** Both distribution forms are
  unsigned, so SmartScreen may warn on first launch.

### Install

Two forms, neither needs a separate .NET or Windows App Runtime install:

1. **Portable zip** — extract `wiina-1.2.4-win-x64.zip`, run `Wiina.exe`.
   Remove it later with `Uninstall Wiina.exe` in the same folder.
2. **Installer** — run `Wiina-Setup-1.2.4-x64.exe`. It installs per user
   (no administrator prompt) and appears in Settings → Apps for uninstall.

Data is stored in `%LOCALAPPDATA%\Wiina`. Details, the uninstall prompt and
the silent switches are in [../installer.md](../installer.md).

### Package contents

```
Wiina.exe
Wiina.dll
libmpv-2.dll
Microsoft.Graphics.Canvas.dll
Microsoft.Graphics.Canvas.Interop.dll
Uninstall Wiina.exe      (portable zip only)
LICENSE
NOTICE
README.md
SOURCE.txt
```

Additional .NET/WinUI publish output is included by the packager; `SOURCE.txt`
records the version, build date, commit, and source URL. Win2D is a dependency
again because the three glass modes use its Gaussian blur.

### Requirements

- Windows 11 22H2 or later (Windows 10 2004+ best effort; titlebar
  customization degrades to the system caption where unsupported)
- x64 CPU
- A GPU/driver with Direct3D 11 support (blur modes need Win2D/D3D11; Solid
  mode works even if Win2D device creation fails)

The build is self-contained; .NET 10 and Windows App Runtime do not need to be
installed separately.

### Upgrade / settings

Settings are stored in `%LOCALAPPDATA%\Wiina\settings.json`. Existing keys are
preserved; no destructive migration is performed. The `ChromeBlurMode` field
accepts `Fixed`, `Dynamic`, `Always`, `Solid`; an unknown value falls back to
Dynamic. The `Language` field accepts `system`, `zh-Hans`, and `en`.

### Known issues

See [known-issues.md](known-issues.md). Accessibility/localization details are
in [../accessibility.md](../accessibility.md); performance numbers in
[../performance.md](../performance.md). The glass modes cost a `screenshot-raw`
readback roughly eight times per second; Solid mode has no see-through by
design.
