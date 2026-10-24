# Accessibility, localization, DPI and motion

This document records what Wiina v1.2.5 implements, how it was checked, and
what is intentionally limited. It is a summary, not a certification.

## 1. Keyboard and focus

- The main window root is a tab stop; `F11` toggles fullscreen and `Esc` leaves
  fullscreen / closes transient chrome (`MainWindow.xaml` accelerators).
- Title-bar and OSC buttons are real `Button` controls, so Tab/Shift+Tab and
  Space/Enter work through WinUI's standard focus handling.
- Settings: the navigation list, page rows (toggles, combo boxes, text boxes,
  action buttons), search box, and key-binding editor are keyboard reachable.
  `Enter` in the settings search commits the first hit; `Esc` clears the search
  and then closes the window. The key-binding recorder uses `Esc` to cancel and
  `Backspace` to clear.
- Open-URL and confirmation dialogs use `ContentDialog` defaults: `Enter`
  accepts the primary button, `Esc` cancels.
- Focus visuals come from the Fluent control templates (`TitleBarButtonStyle`,
  `SettingsNavButtonStyle`, `OscIconButtonStyle`), not from a custom-drawn
  focus rectangle.
- Settings rail pointer rule (1.2.3, tightened in 1.2.4): unselected items
  are pixel-identical at rest and on hover (dim `SecondaryTextBrush`, no fill,
  no border); only the selected item is tinted. Keyboard focus is a 2 px accent
  ring (`UseSystemFocusVisuals=False` plus a custom visual). The rail uses a
  custom `ControlTemplate` whose `PointerOver`/`Pressed` states are empty, and
  the icon/label `Foreground` is bound to the button so no theme fallback colour
  can read as a hover highlight.

## 2. Automation names

Icon-only controls that previously relied on a tooltip now expose
`AutomationProperties.Name` through `loc:Loc.AutomationName` (localized):

| Area | Controls |
|---|---|
| Title bar | pin, layout, OSC position, sidebar, theme, icon mapping, window size, more |
| Top OSC strip | previous, play/pause, next |
| Bottom / floating OSC | previous, play/pause, next, PiP, playlist, quick settings, fullscreen, more |
| Sidebars | close, tabs (quick settings, playlist, chapters, plugins) |
| Timeline / volume | `播放进度` / `音量` (localized) with help text |
| Settings | nav buttons, section-dot buttons, reset/clear actions |

`src/Wiina.Core/Release/AccessibilityMetadata.cs` lists the title-bar keys and
`Stage11ReleaseTests` verifies both the catalog and the XAML wiring.

## 3. High contrast

`src/Wiina.App/Themes/Tokens.xaml` adds a `HighContrast` theme dictionary. All
shell brushes (background, title bar, panels, cards, text, accent, hover /
pressed, sliders, sidebar rows) resolve to Windows system colors:

- `SystemColorWindowColor` / `SystemColorWindowTextColor` for panels and text
- `SystemColorGrayTextColor` for secondary text and slider tracks
- `SystemColorHighlightColor` / `SystemColorHighlightTextColor` for accent and
  selection
- `SystemColorButtonFaceColor` / `SystemColorButtonTextColor` for pressed state

The chrome over the video is either a Win2D blur of the video (固定 / 动态 /
始终, whose tint brushes map to `SystemColorWindowColor` in high contrast) or
the opaque `ChromeSolidBrush` (关闭毛玻璃 and every capture-failure fallback),
which also maps to `SystemColorWindowColor`. See
[ADR-0003](architecture/0003-acrylic.md) for the blur pipeline and
[ADR-0004](architecture/0004-solid-chrome.md) for Solid mode. Standard WinUI controls
(ComboBox, ToggleSwitch, ScrollBar, ContentDialog) use the system high-contrast
templates automatically.

Spot check (Windows 11, “Contrast themes → Aquatic”): panels/text/icons follow
the theme; no hardcoded white-on-white text remains in the shell. Media frames
are user content and are not recolored.

## 4. Reduced motion

Animations run only when **both** the app setting (`Settings → General →
Animations`) and Windows' “Animation effects” setting are enabled:

- `MainWindow.AnimationsEnabled` (title bar/OSC/sidebar/page transitions)
- `OsdText.AnimationsEnabled` (OSD fade/slide)
- `MiniPlayerView.SetAnimationsEnabled`, `PipWindow` transitions

When either is off, chrome changes are instantaneous and OSD text appears
without the slide/fade. Hover highlights are static color changes.

## 5. RTL

v1 ships zh-Hans and English only; no RTL translation is included. The player
surface and the on-screen controller are explicitly pinned to
`FlowDirection="LeftToRight"` in `MainWindow.xaml` (video frame, bottom OSC,
floating OSC, top OSC), and `OsdText` forces LTR for both message branches. A
right-to-left system locale therefore cannot mirror the video, timeline,
volume slider, or OSD.

The settings/welcome/about windows inherit the system flow direction; their
layout has not been validated for Arabic/Hebrew and is documented as
**unsupported** for v1. To test the pinning, set a root element's
`FlowDirection="RightToLeft"` at runtime: the video and OSC remain LTR while
generic text containers mirror.

## 6. DPI and multi-monitor

- XAML uses device-independent pixels (DIPs) and system theme resources. The
  only numeric size tokens (`TitleBarHeight`, `OscButtonSize`, …) are DIPs, not
  physical pixels; no text size is hardcoded in pixels.
- `WindowShell` applies the saved window frame and uses `AppWindow` client
  coordinates, so 100/125/150/200 % scaling is handled by WinUI's DPI scaling.
- Measurement/test method: move the window across monitors with different
  scale factors and check that the title bar/OSC/sidebars remain aligned and
  that playback is not resized on a DPI change. v1 was spot-checked at the
  machine's current scaling (see `performance.md` for the recorded value);
  125/150/200 % are covered by source review of the DIP tokens but were not
  captured as screenshots in this environment because changing the system
  scaling requires a sign-out/session restart.
- Sleep/wake and monitor hot-plug are handled by WinUI window events; they are
  not automatable in this headless check and are documented as manual checks.

## 7. Localization coverage

- Runtime catalog: 550 keys in `UiStringCatalog.cs`, each with a non-empty
  zh-Hans and English string. Coverage includes the title bar, OSC, OSD, menus,
  sidebars, quick settings, all 12 settings pages (titles, summaries, section
  headers, row titles/descriptions/choices), welcome/about/log/inspector/open-URL
  dialogs, filter/equalizer windows, playlist/chapters, mini player, PiP badge,
  and common errors.
- The language row is enabled; changing it applies immediately, re-renders the
  main shell and settings window, and persists `Language` in
  `%LOCALAPPDATA%\Wiina\settings.json`. Already-open auxiliary windows pick up
  the language on their next open.
- English fallback: unsupported system languages fall back to English; a
  concrete unsupported preference behaves like “follow system”. zh-Hans remains
  the default when the system language list has no supported language.
- **Documented exceptions** (kept in Chinese by design):
  - Inspector section/field labels (`MainWindow.Settings.cs`) — diagnostic
    vocabulary that must match mpv property names and is only useful to
    technical users.
  - Filter/encoder/equalizer preset names and codec strings from
    `Wiina.Core/Filters`, `VideoFilters.cs`, `AudioFilters.cs`,
    `TrackInfo.cs`, `InspectorFormatter.cs`.
  - Log viewer raw lines and trace/crash logs.
  - Playback statistics card row labels and group names (文件 / 视频 / 音频 /
    播放 / 渲染) are fixed Chinese strings in `MainWindow.Stats.cs`; the card
    title, subtitle, and close button are localized. Translating the row labels
    is a known follow-up.
  - Plugin/online-subtitle rows remain disabled and explicitly say Stage 9/10
    are skipped; they are not presented as working features.

Coverage test: `Stage11LocalizationTests` fails on a missing English overlay,
an empty value, or any Chinese characters in an English catalog/menu/settings
value. It also verifies the menu, OSD builders, and search-hit localization.

## 8. Known limitations

- Screen-reader announcements for rapid OSD changes are best-effort; there is
  no dedicated live region.
- Custom-drawn icons inside `FontIcon` rely on the button's automation name; the
  glyph itself is not exposed as text.
- The key-binding recorder's status text is not announced automatically.
- No Arabic/Hebrew UI translation in v1 (see RTL above).
- The settings rail deliberately does not brighten on pointer hover; rest and
  hover are identical (see §1).
