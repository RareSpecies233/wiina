# Wiina release notes


Release date (1.2.6): 2026-10-24

Windows-native, IINA-inspired mpv front end built with WinUI 3, Windows App
SDK 2.5, and libmpv.

## What's new in 1.2.6

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
  timeline thumbnails vs Wiina's live per-hover extraction; alignment is
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
