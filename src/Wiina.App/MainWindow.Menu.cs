using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Wiina.App.Views;
using Wiina.Core;
using Wiina.Core.Files;
using Wiina.App.Localization;
using Wiina.Core.Input;
using Wiina.Core.Menu;
using Wiina.Core.Models;
using Wiina.Core.Settings;
using Wiina.Core.Text;
using Wiina.Mpv;

namespace Wiina.App;

/// <summary>
/// Stage 7 application menu: the eight-top-level tree from spec §14 built from
/// <see cref="MenuCatalog"/> with dynamic enable/check state, recent files and
/// live track/device submenus.
/// </summary>
public sealed partial class MainWindow
{
    private void ShowApplicationMenu(FrameworkElement anchor)
    {
        var flyout = new MenuFlyout();
        MenuState state = BuildMenuState();
        foreach (MenuGroupDefinition group in MenuCatalog.Build(state, Loc.CurrentLanguage))
        {
            var top = new MenuFlyoutSubItem { Text = group.Title };
            foreach (MenuItemDefinition item in group.Items)
            {
                AddMenuDefinition(top.Items, item);
            }

            AppendExtraMenuItems(top, group.Id, state);
            flyout.Items.Add(top);
        }

        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Adds post-v1 entries that are window-local instead of living in the
    /// shared MenuCatalog: external audio and the keyboard profile picker.
    /// </summary>
    private void AppendExtraMenuItems(
        MenuFlyoutSubItem top,
        string groupId,
        MenuState state)
    {
        if (string.Equals(groupId, "audio", StringComparison.Ordinal))
        {
            top.Items.Add(new MenuFlyoutSeparator());
            var load = new MenuFlyoutItem
            {
                Text = Loc.T("menu.loadExternalAudio"),
                IsEnabled = state.HasMedia,
            };
            load.Click += async (_, _) => await OpenExternalAudioPickerAsync();
            top.Items.Add(load);
            return;
        }

        if (!string.Equals(groupId, "window", StringComparison.Ordinal))
        {
            return;
        }

        top.Items.Add(new MenuFlyoutSeparator());
        var profiles = new MenuFlyoutSubItem
        {
            Text = Loc.T("menu.keyboardProfile"),
        };
        string current = KeyBindingProfiles.Normalize(_settings.KeyBindingProfile);
        foreach (string profile in KeyBindingProfiles.All)
        {
            string captured = profile;
            string label = ProfileMenuLabel(profile);
            var item = new MenuFlyoutItem
            {
                Text = string.Equals(current, profile, StringComparison.Ordinal)
                    ? "✓ " + label
                    : label,
            };
            item.Click += (_, _) => SwitchKeyBindingProfile(captured);
            profiles.Items.Add(item);
        }

        top.Items.Add(profiles);
    }

    private static string ProfileMenuLabel(string profile) =>
        Loc.T(KeyBindingProfiles.Normalize(profile) switch
        {
            KeyBindingProfiles.Iina => "menu.profileIina",
            KeyBindingProfiles.Custom => "menu.profileCustom",
            _ => "menu.profileDefault",
        });

    private void SwitchKeyBindingProfile(string profile)
    {
        string normalized = KeyBindingProfiles.Normalize(profile);
        _settings.KeyBindingProfile = normalized;
        SaveSettings();
        RebuildKeyMap();
        ShowOsd(Loc.F("osd.keyProfile", ProfileMenuLabel(normalized)));
    }

    private void AddMenuDefinition(
        IList<MenuFlyoutItemBase> target,
        MenuItemDefinition item)
    {
        if (item.Kind == MenuItemKind.Separator)
        {
            target.Add(new MenuFlyoutSeparator());
            return;
        }

        if (item.Kind == MenuItemKind.Submenu)
        {
            var sub = new MenuFlyoutSubItem
            {
                Text = item.Label,
                IsEnabled = item.Enabled,
            };
            if (!item.Enabled && !string.IsNullOrWhiteSpace(item.DisabledReason))
            {
                ToolTipService.SetToolTip(sub, item.DisabledReason);
            }

            foreach (MenuItemDefinition child in item.Children)
            {
                AddMenuDefinition(sub.Items, child);
            }

            AppendDynamicMenuItems(sub, item.Id);
            target.Add(sub);
            return;
        }

        var flyoutItem = new MenuFlyoutItem
        {
            Text = item.IsChecked ? "✓ " + item.Label : item.Label,
            IsEnabled = item.Enabled,
        };
        if (!string.IsNullOrWhiteSpace(item.Shortcut))
        {
            flyoutItem.KeyboardAcceleratorTextOverride = item.Shortcut;
        }

        if (!item.Enabled && !string.IsNullOrWhiteSpace(item.DisabledReason))
        {
            ToolTipService.SetToolTip(flyoutItem, item.DisabledReason);
        }

        string command = item.Command;
        string? argument = item.Argument;
        flyoutItem.Click += (_, _) => ExecuteMenuCommand(command, argument);
        target.Add(flyoutItem);
    }

    private void AppendDynamicMenuItems(MenuFlyoutSubItem sub, string id)
    {
        switch (id)
        {
            case "file.recent":
                AppendRecentItems(sub);
                break;
            case "video.tracks":
                AppendTrackItems(sub, TrackType.Video);
                break;
            case "audio.tracks":
                AppendTrackItems(sub, TrackType.Audio);
                break;
            case "subtitles.tracks":
                AppendTrackItems(sub, TrackType.Subtitle);
                break;
            case "subtitles.secondary":
                AppendSecondarySubtitleItems(sub);
                break;
            case "audio.device":
                AppendAudioDeviceItems(sub);
                break;
        }
    }

    private void AppendRecentItems(MenuFlyoutSubItem sub)
    {
        IReadOnlyList<Wiina.Core.History.HistoryEntry> recent = _historyStore.Recent(10);
        if (recent.Count == 0)
        {
            sub.Items.Add(new MenuFlyoutItem
            {
                Text = Loc.T("menu.noRecent"),
                IsEnabled = false,
            });
            return;
        }

        foreach (Wiina.Core.History.HistoryEntry entry in recent)
        {
            string label = string.IsNullOrWhiteSpace(entry.Title)
                ? System.IO.Path.GetFileName(entry.Path)
                : entry.Title;
            var item = new MenuFlyoutItem { Text = label };
            string path = entry.Path;
            item.Click += (_, _) => OpenPaths(new[] { path });
            sub.Items.Add(item);
        }

        sub.Items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem { Text = Loc.T("menu.clearRecent") };
        clear.Click += (_, _) =>
        {
            App.ClearHistory();
            ShowOsd(Loc.T("menu.recentCleared"));
        };
        sub.Items.Add(clear);
    }

    private void AppendTrackItems(MenuFlyoutSubItem sub, TrackType type)
    {
        var auto = new MenuFlyoutItem
        {
            Text = _trackList.Any(track => track.Type == type && track.IsSelected)
                ? Loc.T("menu.auto")
                : Loc.T("menu.autoChecked"),
        };
        TrackType capturedType = type;
        auto.Click += (_, _) => SetTrackSelection(capturedType, "auto", Loc.T("menu.auto"));
        sub.Items.Add(auto);

        var off = new MenuFlyoutItem { Text = Loc.T("menu.off") };
        off.Click += (_, _) => SetTrackSelection(capturedType, "no", Loc.T("menu.off"));
        sub.Items.Add(off);

        foreach (TrackInfo track in _trackList.Where(track => track.Type == capturedType))
        {
            TrackInfo captured = track;
            string label = captured.DisplayName;
            if (!string.IsNullOrWhiteSpace(captured.Codec))
            {
                label += $"  [{captured.Codec}]";
            }

            var item = new MenuFlyoutItem
            {
                Text = captured.IsSelected ? "✓ " + label : label,
            };
            item.Click += (_, _) => SelectTrack(captured);
            sub.Items.Add(item);
        }
    }

    private void AppendSecondarySubtitleItems(MenuFlyoutSubItem sub)
    {
        var off = new MenuFlyoutItem { Text = Loc.T("menu.off") };
        off.Click += (_, _) => SetSecondarySubtitle(null);
        sub.Items.Add(off);
        foreach (TrackInfo track in _trackList.Where(track => track.Type == TrackType.Subtitle))
        {
            TrackInfo captured = track;
            var item = new MenuFlyoutItem
            {
                Text = _settings.SecondarySubtitleId == captured.Id
                    ? "✓ " + captured.DisplayName
                    : captured.DisplayName,
            };
            item.Click += (_, _) => SetSecondarySubtitle(captured.Id);
            sub.Items.Add(item);
        }
    }

    private void AppendAudioDeviceItems(MenuFlyoutSubItem sub)
    {
        var auto = new MenuFlyoutItem
        {
            Text = string.Equals(_settings.AudioDevice, "auto", StringComparison.OrdinalIgnoreCase)
                ? Loc.T("menu.systemDefaultChecked")
                : Loc.T("menu.systemDefault"),
        };
        auto.Click += (_, _) =>
        {
            _settings.AudioDevice = "auto";
            ApplyMpvString("audio-device", "auto");
            SaveSettings();
            ShowOsd(Loc.T("menu.audioDevice"), Loc.T("menu.systemDefault"));
        };
        sub.Items.Add(auto);

        foreach (MpvAudioDevice device in _audioDevices)
        {
            MpvAudioDevice captured = device;
            var item = new MenuFlyoutItem
            {
                Text = string.Equals(_settings.AudioDevice, captured.Name, StringComparison.Ordinal)
                    ? "✓ " + captured.DisplayName
                    : captured.DisplayName,
            };
            item.Click += (_, _) =>
            {
                _settings.AudioDevice = captured.Name;
                ApplyMpvString("audio-device", captured.Name);
                SaveSettings();
                ShowOsd(Loc.T("menu.audioDevice"), captured.DisplayName);
            };
            sub.Items.Add(item);
        }
    }

    private void ExecuteMenuCommand(string command, string? argument)
    {
        try
        {
            switch (command)
            {
                case MenuCommands.OpenFile:
                    _ = OpenFilePickerAsync();
                    break;
                case MenuCommands.OpenFolder:
                    _ = OpenFolderPickerAsync();
                    break;
                case MenuCommands.OpenUrl:
                case MenuCommands.OpenUrlNewWindow:
                    _ = OpenUrlDialogAsync(command == MenuCommands.OpenUrlNewWindow);
                    break;
                case MenuCommands.OpenInNewWindow:
                    _ = OpenInNewWindowAsync();
                    break;
                case MenuCommands.OpenPlaylist:
                    _ = OpenPlaylistPickerAsync();
                    break;
                case MenuCommands.SavePlaylist:
                    _ = SavePlaylistPickerAsync();
                    break;
                case MenuCommands.ScreenshotFolder:
                    _ = ScreenshotToFolderPickerAsync();
                    break;
                case MenuCommands.CloseWindow:
                    CloseCurrentWindow();
                    break;
                case MenuCommands.Quit:
                    Close();
                    break;

                case MenuCommands.PlayPause:
                    TogglePlayPause();
                    break;
                case MenuCommands.Stop:
                    StopPlayback();
                    break;
                case MenuCommands.StopAndClear:
                    StopPlayback();
                    SendPlayerCommandAsync("playlist-clear");
                    ShowOsd(Loc.T("menu.playlistCleared"));
                    break;
                case MenuCommands.SeekBack5:
                    SeekRelative(-5);
                    break;
                case MenuCommands.SeekForward5:
                    SeekRelative(5);
                    break;
                case MenuCommands.FramePrevious:
                    FrameStep(false);
                    break;
                case MenuCommands.FrameNext:
                    FrameStep(true);
                    break;
                case MenuCommands.JumpToStart:
                    SeekAbsolute(0);
                    break;
                case MenuCommands.Speed:
                    if (double.TryParse(
                            argument,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double speed))
                    {
                        SetSpeed(speed);
                    }

                    break;
                case MenuCommands.SpeedReset:
                    ResetSpeed();
                    break;
                case MenuCommands.Screenshot:
                    CaptureScreenshot();
                    break;
                case MenuCommands.AbLoop:
                    CycleAbLoop();
                    break;
                case MenuCommands.Loop:
                    if (int.TryParse(argument, out int loopMode))
                    {
                        SetLoopMode(loopMode);
                    }

                    break;
                case MenuCommands.Shuffle:
                    ToggleShuffle();
                    break;
                case MenuCommands.PlaylistPrevious:
                    PlaylistPrevious();
                    break;
                case MenuCommands.PlaylistNext:
                    PlaylistNext();
                    break;
                case MenuCommands.ShowPlaylist:
                    ShowPlaylistSidebar();
                    break;
                case MenuCommands.ShowChapters:
                    ShowChaptersSidebar();
                    break;
                case MenuCommands.ChapterPrevious:
                    PreviousChapter();
                    break;
                case MenuCommands.ChapterNext:
                    NextChapter();
                    break;

                case MenuCommands.VideoPanel:
                    ShowSettingsSidebar();
                    SyncQuickSettingsPage(QuickSettingsPage.Video);
                    break;
                case MenuCommands.AudioPanel:
                    ShowSettingsSidebar();
                    SyncQuickSettingsPage(QuickSettingsPage.Audio);
                    break;
                case MenuCommands.SubtitlesPanel:
                    ShowSettingsSidebar();
                    SyncQuickSettingsPage(QuickSettingsPage.Subtitles);
                    break;
                case MenuCommands.VideoHalf:
                    ResizeToScale(0.5);
                    break;
                case MenuCommands.VideoNormal:
                    ResizeToScale(1.0);
                    break;
                case MenuCommands.VideoDouble:
                    ResizeToScale(2.0);
                    break;
                case MenuCommands.VideoFit:
                    FitToScreen();
                    break;
                case MenuCommands.VideoZoomIn:
                    ScaleWindow(1.1);
                    break;
                case MenuCommands.VideoZoomOut:
                    ScaleWindow(0.9);
                    break;
                case MenuCommands.Pip:
                    RequestMode(_mode.Current == PlayerMode.PiP ? PlayerMode.Normal : PlayerMode.PiP);
                    break;
                case MenuCommands.Fullscreen:
                    EnterFullscreenFromMenu();
                    break;
                case MenuCommands.Topmost:
                    TogglePin();
                    break;
                case MenuCommands.LockAspect:
                    _settings.LockAspectRatio = !_settings.LockAspectRatio;
                    SaveSettings();
                    ShowOsd(_settings.LockAspectRatio ? Loc.T("menu.aspectLocked") : Loc.T("menu.aspectUnlocked"));
                    break;
                case MenuCommands.MusicMode:
                    RequestMode(_mode.Current == PlayerMode.Mini ? PlayerMode.Normal : PlayerMode.Mini);
                    break;
                case MenuCommands.Aspect:
                    if (!string.IsNullOrWhiteSpace(argument))
                    {
                        _settings.VideoAspect = QuickSettings.NormalizeAspect(argument);
                        ApplyMpvString("video-aspect-override", _settings.VideoAspect);
                        SaveSettings();
                        ShowOsd(Loc.T("menu.aspect"), _settings.VideoAspect == "no" ? Loc.T("menu.auto") : _settings.VideoAspect);
                    }

                    break;
                case MenuCommands.Crop:
                    if (double.TryParse(
                            argument,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out double crop))
                    {
                        _settings.VideoCropAspect = crop;
                        ApplyCropFromSettings();
                        SaveSettings();
                    }

                    break;
                case MenuCommands.Rotate:
                    if (int.TryParse(argument, out int rotation))
                    {
                        _settings.VideoRotation = QuickSettings.NormalizeRotation(rotation);
                        ApplyMpvString(
                            "video-rotate",
                            _settings.VideoRotation.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        SaveSettings();
                    }

                    break;
                case MenuCommands.FlipHorizontal:
                    _settings.VideoFlipHorizontal = !_settings.VideoFlipHorizontal;
                    ApplyVideoFilters();
                    SaveSettings();
                    break;
                case MenuCommands.FlipVertical:
                    _settings.VideoFlipVertical = !_settings.VideoFlipVertical;
                    ApplyVideoFilters();
                    SaveSettings();
                    break;
                case MenuCommands.Deinterlace:
                    _settings.VideoDeinterlace = !_settings.VideoDeinterlace;
                    ApplyMpvString("deinterlace", QuickSettings.DeinterlaceValue(_settings.VideoDeinterlace));
                    SaveSettings();
                    break;

                case MenuCommands.VolumeUp5:
                    AdjustVolume(5);
                    break;
                case MenuCommands.VolumeDown5:
                    AdjustVolume(-5);
                    break;
                case MenuCommands.VolumeUp1:
                    AdjustVolume(1);
                    break;
                case MenuCommands.VolumeDown1:
                    AdjustVolume(-1);
                    break;
                case MenuCommands.Mute:
                    ToggleMute();
                    break;
                case MenuCommands.AudioDelayDown:
                    AdjustAudioDelay(-0.1);
                    break;
                case MenuCommands.AudioDelayUp:
                    AdjustAudioDelay(0.1);
                    break;
                case MenuCommands.AudioDelayReset:
                    _settings.AudioDelay = 0;
                    ApplyMpvString("audio-delay", "0");
                    SaveSettings();
                    ShowOsd(Loc.T("menu.audioDelayReset"));
                    break;

                case MenuCommands.SubtitleToggle:
                    ToggleSubtitleVisibility();
                    break;
                case MenuCommands.LoadSubtitle:
                    _ = OpenSubtitlePickerAsync();
                    break;
                case MenuCommands.SubtitleDelayDown:
                    AdjustSubtitleDelay(-0.1);
                    break;
                case MenuCommands.SubtitleDelayUp:
                    AdjustSubtitleDelay(0.1);
                    break;
                case MenuCommands.SubtitleDelayReset:
                    ResetSubtitleDelay();
                    break;
                case MenuCommands.SubtitlePositionUp:
                    AdjustSubtitlePosition(5);
                    break;
                case MenuCommands.SubtitlePositionDown:
                    AdjustSubtitlePosition(-5);
                    break;
                case MenuCommands.SubtitleScaleUp:
                    AdjustSubtitleScale(0.1);
                    break;
                case MenuCommands.SubtitleScaleDown:
                    AdjustSubtitleScale(-0.1);
                    break;
                case MenuCommands.SubtitleScaleReset:
                    _settings.SubtitleScale = 1;
                    ApplyMpvString("sub-scale", "1");
                    SaveSettings();
                    ShowOsd(Loc.T("menu.subtitleScaleReset"));
                    break;
                case MenuCommands.SubtitleFont:
                    _ = PickSubtitleFontAsync();
                    break;
                case MenuCommands.SubtitleSettings:
                    App.ShowSettingsWindow(SettingsPageId.Subtitles, null);
                    break;

                case MenuCommands.PluginFolder:
                    App.OpenFolder(App.PluginsFolder);
                    break;

                case MenuCommands.NewWindow:
                    NewWindow();
                    break;
                case MenuCommands.OpenInNew:
                    _ = OpenInNewWindowAsync();
                    break;
                case MenuCommands.Minimize:
                    if (AppWindow.Presenter is OverlappedPresenter minimizable)
                    {
                        minimizable.Minimize();
                    }

                    break;
                case MenuCommands.Maximize:
                    if (AppWindow.Presenter is OverlappedPresenter presenter)
                    {
                        if (presenter.State == OverlappedPresenterState.Maximized)
                        {
                            presenter.Restore();
                        }
                        else
                        {
                            presenter.Maximize();
                        }
                    }

                    break;
                case MenuCommands.Inspector:
                    App.ShowInspectorWindow();
                    break;
                case MenuCommands.VideoFilters:
                    App.ShowFiltersWindow(audioTab: false);
                    break;
                case MenuCommands.AudioFilters:
                    App.ShowFiltersWindow(audioTab: true);
                    break;
                case MenuCommands.LogViewer:
                    App.ShowLogViewerWindow();
                    break;
                case MenuCommands.Settings:
                    App.ShowSettingsWindow(SettingsPageId.General, null);
                    break;

                case MenuCommands.Welcome:
                    App.ShowWelcomeWindow();
                    break;
                case MenuCommands.About:
                    App.ShowAboutWindow();
                    break;
                case MenuCommands.Shortcuts:
                    App.ShowSettingsWindow(SettingsPageId.KeyBindings, null);
                    break;
                case MenuCommands.LogFolder:
                    App.OpenFolder(App.LogsFolder);
                    break;
                case MenuCommands.ExportDebug:
                {
                    string path = App.ExportDiagnostics();
                    ShowOsd(Loc.T("menu.debugExported"), path);
                    break;
                }

                case MenuCommands.ProjectPage:
                    _ = Launcher.LaunchUriAsync(new Uri(Branding.ProjectUrl));
                    break;

                default:
                    Trace("unhandled menu command: " + command);
                    break;
            }
        }
        catch (Exception ex)
        {
            Trace($"menu command failed ({command}): {ex}");
            ShowOsd(Loc.T("menu.operationFailed"), ex.Message);
        }
    }

    private async Task AddFilesToPlaylistAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.VideosLibrary,
            };
            picker.FileTypeFilter.Add("*");
            InitializePicker(picker);
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
            int added = 0;
            foreach (StorageFile file in files)
            {
                if (MediaFileTypes.IsPlayableMedia(file.Path))
                {
                    SendPlayerCommandAsync("loadfile", file.Path, "append");
                    added++;
                }
            }

            if (added > 0)
            {
                ShowOsd(Loc.F("menu.filesAdded", added));
            }
        }
        catch (Exception ex)
        {
            Trace("append files failed: " + ex);
            ShowOsd(Loc.T("menu.addFailed"), ex.Message);
        }
    }
    private async Task PickSubtitleFontAsync()
    {
        string? picked = await Dialogs.PickFontAsync(RootGrid.XamlRoot, _settings.SubtitleFont);
        if (picked is null)
        {
            return;
        }

        _settings.SubtitleFont = picked;
        ApplyMpvString("sub-font", picked);
        SaveSettings();
        ShowOsd(Loc.T("menu.subtitleFont"), picked);
    }
}
