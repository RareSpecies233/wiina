# Wiina v1.2.4 — release notes

Release date: 2026-10-24

Windows-native, IINA-inspired mpv front end built with WinUI 3, Windows App
SDK 2.5, and libmpv.

## What's new in 1.2.4

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
- OSD message text stays opaque in every mode, and in the Docked layout the
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

## Changes since 1.2.3 worth knowing

- The 1.2.3 "solid chrome only" design is history: the blur pipeline, Win2D
  package and `ui.blur-mode` row are back. The setting is `ChromeBlurMode`
  again; a 1.2.3 `settings.json` that stored `"ChromeBlurMode": "Solid"`
  stays Solid until changed.
- Persistent technical OSD is gone for good; the statistics card replaces it.
- PiP sizes stored by 1.2.3 are still valid; the render cap that used to clamp
  them no longer exists, so a restored PiP window renders at full resolution.
- 1.2.3 remains in git history (PR #2) and its acceptance record is kept as a
  historical document.

## Intentionally not included

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

## Install

Two forms, neither needs a separate .NET or Windows App Runtime install:

1. **Portable zip** — extract `wiina-1.2.4-win-x64.zip`, run `Wiina.exe`.
   Remove it later with `Uninstall Wiina.exe` in the same folder.
2. **Installer** — run `Wiina-Setup-1.2.4-x64.exe`. It installs per user
   (no administrator prompt) and appears in Settings → Apps for uninstall.

Data is stored in `%LOCALAPPDATA%\Wiina`. Details, the uninstall prompt and
the silent switches are in [../installer.md](../installer.md).

## Package contents

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

## Requirements

- Windows 11 22H2 or later (Windows 10 2004+ best effort; titlebar
  customization degrades to the system caption where unsupported)
- x64 CPU
- A GPU/driver with Direct3D 11 support (blur modes need Win2D/D3D11; Solid
  mode works even if Win2D device creation fails)

The build is self-contained; .NET 10 and Windows App Runtime do not need to be
installed separately.

## Upgrade / settings

Settings are stored in `%LOCALAPPDATA%\Wiina\settings.json`. Existing keys are
preserved; no destructive migration is performed. The `ChromeBlurMode` field
accepts `Fixed`, `Dynamic`, `Always`, `Solid`; an unknown value falls back to
Dynamic. The `Language` field accepts `system`, `zh-Hans`, and `en`.

## Known issues

See [known-issues.md](known-issues.md). Accessibility/localization details are
in [../accessibility.md](../accessibility.md); performance numbers in
[../performance.md](../performance.md). The glass modes cost a `screenshot-raw`
readback roughly eight times per second; Solid mode has no see-through by
design.
