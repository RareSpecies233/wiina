# Performance measurements (v1.3.2)

Measured on the development machine on 2026-10-19 (Asia/Hong_Kong), at v1.0.0.
The numbers below are the historical baseline; they were **not** re-measured
for 1.2.4 through 1.3.2 (see the per-version notes below for
the known deltas). Numbers are what
this machine produced; they are not universal guarantees.

## Environment

| Item | Value |
|---|---|
| OS | Windows 11 (development host) |
| CPU / RAM | development host — see below |
| Build | Release, self-contained `win-x64` |
| Binary | `src\Wiina.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Wiina.exe` |
| libmpv | shinchiro `20261018-git-01e2533f31` (mpv 0.41+) |
| Display scaling | 100 % (see DPI note) |

The exact host model/CPU was not captured by the measurement script; do not
cite these numbers for other hardware without re-running the method.

## Method

1. Kill any running `Wiina` process.
2. Start a `Stopwatch`, then `Process.Start(exe, mediaPath)` (or no argument for
   the welcome window).
3. Poll `Process.MainWindowHandle` every 25 ms until it becomes non-zero; that
   is the "window shown" time. This is **not** a frame-rendered or
   first-media-frame measurement.
4. Let the app settle for 3–5 s, then read `WorkingSet64` and
   `PrivateMemorySize64`.
5. Repeat each scenario 2–3 times; report the range.

Test media were synthetic, generated locally into `%TEMP%` and never committed:
uncompressed RGB24 AVI, 1920×1080 (≈35.6 MB, 6 frames) and 3840×2160
(≈71.2 MB, 3 frames). Synthetic uncompressed video is intentionally cheap to
decode, so it exercises startup/UI and the composition swapchain more than a
real codec's decode cost.

## Results

### Cold/idle start (no media)

| Run | Window shown | Working set | Private bytes |
|---|---|---|---|
| 1 (first after build) | 0.95 s | 195.2 MB | 147.9 MB |
| 2 | 0.54 s | 195.6 MB | 147.7 MB |
| 3 | 0.53 s | 194.4 MB | 146.8 MB |

Cold start target: **< 3 s** → met (0.95 s worst window-shown time).

### 1080p playback

| Run | Window shown | Working set | Private bytes |
|---|---|---|---|
| 1 | 0.53 s | 239.7 MB | 248.9 MB |
| 2 | 0.53 s | 237.6 MB | 250.9 MB |
| 3 | 0.54 s | 234.9 MB | 251.2 MB |

### 4K (2160p) playback

| Run | Window shown | Working set | Private bytes |
|---|---|---|---|
| 1 | 0.54 s | 265.7 MB | 422.8 MB |
| 2 | 0.53 s | 259.2 MB | 421.3 MB |

Idle memory target: **< 300 MB** → met for working set in all measured
scenarios (peak 265.7 MB at 4K). Private bytes exceed 300 MB at 4K because the
GPU/driver surfaces and the video swapchain are charged to the process; this is
reported honestly rather than trimmed by a smaller measurement window.

## What changed in 1.3.2

- **Previews off means zero thumbnail work.** With the preview master switch
  off, `ThumbnailService` is not created at all: no headless mpv instance, no
  cache sizing, no disk writes, no pre-generation task. This is the cheapest
  setting for low-end devices and removes the residual `mpv` process cost that
  used to remain after disabling previews.
- **The thumbnail cache-count row was removed.** The memory cache always sizes
  itself to every 5-second bucket of the current file (≈57.6 KB per bucket);
  the disk budget row (`video.thumbnail-cache-mb`) is unchanged.
- **Closing is fast and single-pass.** K01 measured 170-179 ms for the close
  handler and 362-426 ms until the process exits, with one `SaveSettings()` and
  no watchdog. No new per-frame work: the cursor IAT hook only runs while the
  pointer is hidden, the fullscreen cover work happens once per presenter
  change, and the menu/OSC changes are event-driven.
- **The device-loss fallback is bounded.** `DeviceLoss` recovery (2-minute
  window, 5-second cooldown, limited attempts) only runs on a classified
  `0x887A0005/6/7/20`, so the steady-state cost is zero. The recovery was not
  exercised by a real TDR.
- Counts for this snapshot: **11 settings pages / 128 rows**, 568 localization
  keys, **827** Core tests. Startup, memory, blur and 4K numbers were **not**
  re-measured for 1.3.2.
## What changed in 1.3.1

- **Thumbnail decode can use the GPU.** The new `video.thumbnail-acceleration`
  setting selects Auto (`hwdec=auto-safe`) or CPU only (`hwdec=no`) for the
  headless thumbnail instance. Maintainer measurement on a 1080p HEVC file:
  Auto ≈ **29 ms wall / 15.6 ms CPU per frame**, CPU only ≈ **104 ms / 461 ms**.
  Auto falls back to software decode when the driver cannot use the GPU, so the
  worst case equals CPU-only.
- **The in-memory thumbnail cache covers the whole current file.** Every 5 s
  bucket is kept: about **57.6 KB per bucket**, i.e. ≈**41 MB per hour** —
  24 minutes ≈16 MB, 2 hours ≈83 MB. This is RAM only; the disk budget is still
  `ui.thumbnail-cache-mb` (default 512 MB, LRU by bytes).
- **Screenshots with/without subtitles** only change the mpv screenshot flags;
  no measurable per-frame cost.
- **No new per-frame work.** The menu merge, speed flyout, ±5 s previous/next
  and the pointer-leave hide are all event-driven. The uninstaller elevation
  adds nothing at runtime.
- Counts for this snapshot: **11 settings pages / 129 rows**, 563 localization
  keys, **780** Core tests. Startup, memory, blur and 4K numbers were **not**
  re-measured for 1.3.1.

## What changed in 1.3.0

1.3.0 adds no per-frame work. The settings reshuffle is a one-time catalog
change; the key binding editor is modal; SMTC is event-driven; the frame-drop
detector reuses one timer and a 3-second window; slow thumbnail mode *reduces*
background I/O; and no new package references were added. Numbers below were
not re-measured for 1.3.0.
## What changed in 1.2.7

- **Thumbnail pre-generation runs once per file, off the UI thread.** After load,
  the single extraction worker walks the timeline in 5 s buckets at 90 px tall
  and writes JPEGs through `ThumbnailDiskCache`; hovering then costs a disk/memory
  lookup instead of a seek+decode. Measured on the maintainer's 1080p 23:40 HEVC
  file: 285 buckets in 14.1–14.9 s cold, 0.3 s when the cache is warm; hover
  73 ms cold / 4 ms warm; 1.70 MB on disk (~6 KB per frame).
- **Disk cost is bounded by configuration, not by file length.** Default budget
  512 MB (Ui → Thumbnails → Thumbnail cache, 0–4096 MB), byte-based LRU, one
  `index.json` rewritten atomically per batch; format-version directory
  (`thumbnails\v2`) means old layouts are discarded rather than migrated.
- **Cursor hiding is attempted once per chrome transition, not per frame.** The
  implementation is a state change (no loop), but under WinUI 3 the desktop
  pointer is owned by CoreMessaging and the attempt is currently ineffective;
  see known-issues for the root cause and the resource-based follow-up.
- **The 0.5×/1×/2× sizing is one read + one move.** `video-params/dw|dh` is read
  when the command runs, converted once and clamped; no per-resize tracking.
- **Frame style is cached per presenter/state.** `SetWindowFrame` is called when
  the state changes (snap, fullscreen, presenter change), not on every resize
  message.
- **Settings: 12 pages / 125 rows in 1.2.7** (three new rows: thumbnail cache
  size, clear cache, administrator association). Page construction still shows no
  visible stall.
- **Tests: 711 passed / 0 failed** (maintainer's Release verification; this docs
  pass did not build or re-run them).
- Startup, memory, blur and 1080p/4K numbers were not re-measured for 1.2.7; the
  1.0.0 baseline and the 1.2.4/1.2.5/1.2.6 notes above remain the last measured
  values.
## What changed in 1.2.6

- **Auto-hide idle tracking is one monotonic clock, not polling.** The main
  window keeps a single `Stopwatch`; the `DispatcherQueueTimer` fires at the
  configured delay and only asks `OscVisibilityController.ShouldHideOnIdle`.
  Pointer moves under 5 DIP on both axes are dropped before they touch the
  clock, so jitter no longer restarts the timer. Nothing is measured per frame.
- **OSC placement is recomputed on layout events only** - window resize, sidebar
  panel `SizeChanged`, floating-OSC/canvas size change, drag end and position
  restore. The clamp reads the visible panel widths and performs one rectangle
  overlap test; there is no per-pointer-move work.
- **The audio-device dropdown does no polling.** It projects the main window's
  cached `audio-device-list` (mpv pushes it on start and when the device set
  changes) when the settings page is built and on explicit refresh; selection
  goes out as one mpv `audio-device` command.
- **Settings: 12 pages / 122 rows in 1.2.6** (one new row: auto-hide delay; 1.2.7 adds three more, see above).
  Building a page still shows no visible stall.
- **Tests: 668 passed / 0 failed** (maintainer's release verification; the docs
  pass did not build or re-run them).
- Startup, memory and blur cost were not re-measured for 1.2.6; the 1.2.4/1.2.5
  numbers above remain the last measured values.

## What changed in 1.2.5

- **Live theme switching costs one recolor, not a re-render.** `App.ResolveTheme`
  pushes the new `ElementTheme` to every open window and `ChromeTint.Recolor`
  re-mixes the opaque `ChromeSolidBrush` (16 % accent). That is a handful of
  `SolidColorBrush` writes plus one layout/repaint per window per switch; there
  is no per-frame work, and a `ThemeMode.System` change follows the Windows app
  theme through `UISettings.ColorValuesChanged` instead of polling.
- **The OSD card joins the blur pipeline.** In the three glass modes the OSD is
  one more `ChromeBlurLayer` crop (320 px blurred frame, shared with the other
  surfaces), so it adds no extra capture; in Solid it adds no work at all.
- **Solid is now opaque by construction.** Chrome panels no longer write
  `Background` from code; the brush stays declared in XAML and the blur/tint
  layers toggle `Visibility`. Cost is unchanged (one fill per surface).
- **Settings rows: 12 pages / 122 rows in 1.2.6.** (1.2.4 and 1.2.5 had 121;
  earlier drafts of this page said 119, which was off by two.) The new 1.2.6 row
  is the OSC auto-hide delay. The quality editor adds no settings rows — its 23
  options live in the profile window.
- **Tests: 659 passed / 0 failed** (maintainer release verification for 1.2.5;
  the docs pass did not re-run them).
- Startup/memory were not re-measured for 1.2.5.

## What changed in 1.2.4

### Chrome blur cost depends on the mode

| Mode | Capture work | Approximate cost |
|---|---|---|
| 固定（静态帧） | one `screenshot-raw` per chrome show | one frame's readback per show |
| 动态（跟随视频） | ~8 fps while the chrome is visible | ~45 ms / 3.7 MB per 1280×720 frame, ≈⅓ core at 720p |
| 始终（持续计算） | ~8 fps whenever the player runs | same as Dynamic, continuing while chrome is hidden |
| 关闭毛玻璃（纯色） | none — no worker, no Win2D device | one solid fill per surface |

The 1.0.0–1.2.2 measurements above were taken with the blur pipeline active
(Dynamic), so they already include its steady-state cost. The 1.2.3 release
temporarily removed the pipeline and therefore cost less; 1.2.4 restores it, so
treat the historical numbers as the “blur on” baseline and Solid as strictly
cheaper. There is no separate measurement of Solid on this host — its per-frame
work is one brush fill.

### PiP / mini player render at full resolution

- The 640×360 compositor-upscale cap is gone. `PipGeometry.RenderSize` now
  returns the window's full physical pixel size, so a large PiP window asks mpv
  to render (and the compositor to present) more pixels than before. That is a
  real GPU/compositor cost increase for big PiP windows — it buys a sharp
  picture, and it is not a capture readback.
- The mini player was never capped; it still saves cost only through its
  simplified UI (no blur, no shadow over the video).

### Snapping and DWM queries

- `WindowInterop.TryGetFrameMargins` calls `DwmGetWindowAttribute` once per
  window/DPI pair and caches the result; the cache is invalidated on
  `WM_DPICHANGED` and on window changes that can alter the frame. Snapping
  therefore does not add a per-frame or per-tick DWM query.
- Snapping itself is position math on `WM_MOVING`; it adds no rendering work.

### Statistics panel

- The native WinUI card reads about 30 mpv properties once per second, renders
  a normal XAML list of 29 rows, and fills immediately when enabled rather than
  waiting for a live frame. The old path pushed a four-field string through
  `show-text`, competing with every other OSD message.
- The card now follows the OSD placement setting and re-applies the theme at
  runtime; that is a layout pass per OSD-position change, not per tick.

### Settings and packaging

- Settings stayed at **12 pages / 121 rows** (same as 1.2.3): the blur-mode row was
  restored and the deband row moved to 视频 → 画面. Row construction is still
  code-generated and showed no visible stall.
- The settings rail no longer animates a hover brush on unselected items, so it
  does strictly less work per pointer move.
- `scripts/package.ps1` now publishes the trimmed single-file uninstaller
  first (a small, independent build) and verifies it inside the payload before
  zipping; this adds a few seconds to packaging, not to app startup.
- Core tests: 644 passed / 0 failed (Lead's release verification; not re-run by
  the docs pass).
- Startup/memory were not re-measured for this version.

## UI-thread behavior

- Thumbnails (scrub peek) and the inspector run off the UI thread; the UI shows
  placeholder/loading state while data arrives. No synchronous file or IPC work
  was observed blocking the window during the measurements.
- Settings/page navigation builds rows in code; 11 page destinations (12 catalog
  entries, KeyBindings redirects) with 129 rows total render without a visible
  stall on this machine.
- Library scanning is not a feature of this stage; playlist import is bounded
  by the files the user opens.

## DPI

The measurements above were taken at the machine's scaling (100 %). 125/150/200 %
were reviewed in source (all sizes are DIPs; no pixel constants), but were not
re-measured here because changing system scaling requires a session
sign-out/restart. `docs/accessibility.md` documents the manual check. Snapping
scales its 10 DIP / 22 DIP thresholds by the monitor scale factor, and DWM frame
margins are cached per DPI, but mixed-DPI monitor moves remain a manual check.

## HDR and tone-mapping limits

- Stage 8's HDR path passes mpv's `target-colorspace-hint` /
  `--target-colorspace-hint` and uses the Windows composition swapchain. The
  swapchain is **SDR (8-bit)** in v1 — HDR content is tone-mapped to SDR by mpv,
  not presented as a true HDR10 swapchain.
- True HDR output needs a `DXGI_FORMAT_R10G10B10A2_UNORM` (or FP16) swapchain,
  an HDR-aware `SwapChainPanel` composition path, and per-monitor HDR state
  handling. That is out of scope for v1.
- Tone-mapping can be influenced by mpv options exposed in Advanced; it is not a
  calibrated HDR pipeline and should not be used for color-critical work.
- Screenshots/thumbnails are SDR PNGs.

## How to reproduce

```powershell
# generate the same synthetic clips (not committed; writes to %TEMP%)
pwsh -File .scratch/gen-avi.ps1   # local helper, keep out of git

# manual timing (example)
$exe = "src\Wiina.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Wiina.exe"
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process $exe -ArgumentList "$env:TEMP\wiina-test-1080p.avi" -PassThru
while ($p.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 25; $p.Refresh() }
$sw.Elapsed.TotalSeconds
$p.Refresh(); $p.WorkingSet64 / 1MB; $p.PrivateMemorySize64 / 1MB
```
