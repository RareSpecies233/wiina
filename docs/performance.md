# Performance measurements (v1.0.0)

Measured on the development machine on 2026-10-19 (Asia/Hong_Kong). Numbers are
what this machine produced; they are not universal guarantees.

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

## UI-thread behavior

- Thumbnails (scrub peek) and the inspector run off the UI thread; the UI shows
  placeholder/loading state while data arrives. No synchronous file or IPC work
  was observed blocking the window during the measurements.
- Settings/page navigation builds rows in code; 11 pages with 105 rows total
  render without a visible stall on this machine.
- Library scanning is not a feature of this stage; playlist import is bounded
  by the files the user opens.

## DPI

The measurements above were taken at the machine's scaling (100 %). 125/150/200 %
were reviewed in source (all sizes are DIPs; no pixel constants), but were not
re-measured here because changing system scaling requires a session
sign-out/restart. `docs/accessibility.md` documents the manual check.

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
