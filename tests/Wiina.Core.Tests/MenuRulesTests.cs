using Wiina.Core.Menu;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class MenuRulesTests
{
    private static MenuItemDefinition Find(MenuState state, string id) =>
        MenuCatalog.Flatten(MenuCatalog.Build(state)).Single(item => item.Id == id);

    [Fact]
    public void EightTopLevelMenusKeepWindowAndVideoWorkSeparate()
    {
        IReadOnlyList<MenuGroupDefinition> groups = MenuCatalog.Build(new MenuState());
        Assert.Equal(
            new[] { "文件", "播放", "视频", "音频", "字幕", "插件", "窗口", "帮助" },
            groups.Select(group => group.Title));
        Assert.All(groups, group => Assert.NotEmpty(group.Items));
    }

    [Fact]
    public void WindowColumnsHoldOnlyWindowAndLayoutOperations()
    {
        IReadOnlyList<MenuGroupDefinition> groups = MenuCatalog.Build(new MenuState());
        MenuGroupDefinition window = groups.Single(group => group.Title == "窗口");
        MenuGroupDefinition video = groups.Single(group => group.Title == "视频");

        // Window/layout operations stay together in 窗口 ...
        string[] windowIds = window.Items.Select(item => item.Id).ToArray();
        Assert.Contains("window.half", windowIds);
        Assert.Contains("window.fit", windowIds);
        Assert.Contains("video.fullscreen", windowIds);
        Assert.Contains("video.topmost", windowIds);
        Assert.Contains("video.lock-aspect", windowIds);
        // PiP stays in 窗口 (it swaps the window for the floating player);
        // mini/music mode is no longer reachable from any menu.
        Assert.Contains("video.pip", windowIds);
        Assert.DoesNotContain("video.music-mode", windowIds);

        // ... while the pure picture family never appears there.
        Assert.DoesNotContain("video.aspect", windowIds);
        Assert.DoesNotContain("video.crop", windowIds);
        Assert.DoesNotContain("video.rotate", windowIds);
        Assert.DoesNotContain("video.flip-h", windowIds);
        Assert.DoesNotContain("video.deinterlace", windowIds);
        Assert.DoesNotContain("video.filters", windowIds);
        Assert.DoesNotContain("video.screenshot", windowIds);
        Assert.DoesNotContain("video.panel", windowIds);
        Assert.DoesNotContain("video.tracks", windowIds);

        string[] videoIds = video.Items.Select(item => item.Id).ToArray();
        Assert.Contains("video.aspect", videoIds);
        Assert.Contains("video.crop", videoIds);
        Assert.Contains("video.rotate", videoIds);
        Assert.Contains("video.flip-h", videoIds);
        Assert.Contains("video.flip-v", videoIds);
        Assert.Contains("video.deinterlace", videoIds);
        Assert.Contains("video.filters", videoIds);
        Assert.Contains("video.screenshot", videoIds);
        Assert.Contains("video.panel", videoIds);
        Assert.Contains("video.tracks", videoIds);
    }

    [Fact]
    public void NoMediaDisablesPlaybackButKeepsFileActions()
    {
        var state = new MenuState();
        Assert.False(Find(state, "playback.play-pause").Enabled);
        Assert.False(Find(state, "playback.seek-back").Enabled);
        Assert.False(Find(state, "video.screenshot").Enabled);
        Assert.False(Find(state, "audio.mute").Enabled);
        Assert.False(Find(state, "subtitles.load").Enabled);
        Assert.True(Find(state, "file.open").Enabled);
        Assert.True(Find(state, "file.close").Enabled);
        Assert.True(Find(state, "window.settings").Enabled);
        Assert.True(Find(state, "video.fullscreen").Enabled);
        Assert.True(Find(state, "window.maximize").Enabled);
    }

    [Fact]
    public void MediaEnablesPlaybackActions()
    {
        var state = new MenuState { HasMedia = true };
        Assert.True(Find(state, "playback.play-pause").Enabled);
        Assert.True(Find(state, "playback.stop").Enabled);
        Assert.True(Find(state, "playback.seek-back").Enabled);
        Assert.True(Find(state, "playback.frame-next").Enabled);
        Assert.True(Find(state, "audio.mute").Enabled);
        Assert.True(Find(state, "subtitles.load").Enabled);
    }

    [Fact]
    public void PlayPauseLabelFollowsPlayingState()
    {
        Assert.Equal(
            "继续播放",
            Find(new MenuState { HasMedia = true, IsPlaying = false }, "playback.play-pause").Label);
        Assert.Equal(
            "暂停",
            Find(new MenuState { HasMedia = true, IsPlaying = true }, "playback.play-pause").Label);
    }

    [Fact]
    public void SubtitleCommandsNeedASubtitleTrack()
    {
        var without = new MenuState { HasMedia = true };
        Assert.False(Find(without, "subtitles.toggle").Enabled);
        Assert.False(Find(without, "subtitles.delay-up").Enabled);
        Assert.False(Find(without, "subtitles.scale-down").Enabled);
        Assert.False(Find(without, "subtitles.tracks").Enabled);

        var with = new MenuState
        {
            HasMedia = true,
            HasSubtitleTrack = true,
            SubtitlesVisible = false,
        };
        Assert.True(Find(with, "subtitles.toggle").Enabled);
        Assert.Equal("显示字幕", Find(with, "subtitles.toggle").Label);
        Assert.True(Find(with, "subtitles.delay-up").Enabled);
        Assert.True(Find(with, "subtitles.tracks").Enabled);
    }

    [Fact]
    public void PlaylistNavigationNeedsMoreThanOneItem()
    {
        Assert.False(Find(new MenuState { HasMedia = true, PlaylistCount = 1 }, "playback.next").Enabled);
        Assert.True(Find(new MenuState { HasMedia = true, PlaylistCount = 2 }, "playback.next").Enabled);
        Assert.True(Find(new MenuState { HasMedia = true, PlaylistCount = 2 }, "playback.previous").Enabled);
        Assert.False(Find(new MenuState { HasMedia = true }, "playback.stop-clear").Enabled);
        Assert.True(Find(new MenuState { HasMedia = true, PlaylistCount = 1 }, "playback.stop-clear").Enabled);
    }

    [Fact]
    public void ChapterCommandsNeedChapters()
    {
        Assert.False(Find(new MenuState { HasMedia = true }, "playback.chapter-next").Enabled);
        Assert.True(Find(new MenuState { HasMedia = true, ChapterCount = 3 }, "playback.chapter-next").Enabled);
        Assert.True(Find(new MenuState { HasMedia = true, ChapterCount = 3 }, "playback.chapter-previous").Enabled);
    }

    [Fact]
    public void PluginMenuStaysDisabledUntilStageNine()
    {
        var state = new MenuState { HasMedia = true };
        MenuItemDefinition manage = Find(state, "plugin.manage");
        Assert.False(manage.Enabled);
        Assert.Contains("Stage 9", manage.DisabledReason);
        Assert.False(Find(state, "plugin.install").Enabled);
        Assert.True(Find(state, "plugin.folder").Enabled);

        var ready = new MenuState { PluginHostAvailable = true };
        Assert.True(Find(ready, "plugin.manage").Enabled);
        Assert.True(Find(ready, "plugin.install").Enabled);
    }

    [Fact]
    public void StageEightFiltersAreEnabledAndStageTenItemsAreDisabled()
    {
        var state = new MenuState { HasMedia = true };
        MenuItemDefinition filters = Find(state, "video.filters");
        Assert.True(filters.Enabled);
        Assert.True(string.IsNullOrEmpty(filters.DisabledReason));

        Assert.True(Find(state, "audio.filters").Enabled);

        MenuItemDefinition online = Find(state, "subtitles.online");
        Assert.False(online.Enabled);
        Assert.Contains("Stage 10", online.DisabledReason);
    }

    [Fact]
    public void FullscreenItemLabelFollowsState()
    {
        var fullscreen = new MenuState { HasMedia = true, IsFullscreen = true };
        Assert.True(Find(fullscreen, "video.fullscreen").IsChecked);
        Assert.Equal("退出全屏", Find(fullscreen, "video.fullscreen").Label);

        // The mini/music menu entry was removed (mini mode now lives only under
        // Settings → 高级 → 模式); PiP keeps its 窗口 entry.
        string[] ids = MenuCatalog
            .Flatten(MenuCatalog.Build(new MenuState()))
            .Select(item => item.Id)
            .ToArray();
        Assert.DoesNotContain("video.music-mode", ids);
        Assert.Contains("video.pip", ids);
    }

    [Fact]
    public void CheckStatesMirrorSettings()
    {
        var state = new MenuState
        {
            HasMedia = true,
            AlwaysOnTop = true,
            LockAspect = false,
            Muted = true,
            Shuffle = true,
            AbLoopActive = true,
            LoopMode = 2,
            Speed = 1.5,
            Aspect = "4:3",
            CropAspect = 1.25,
            Rotation = 90,
            FlipHorizontal = true,
            Deinterlace = true,
        };
        Assert.True(Find(state, "video.topmost").IsChecked);
        Assert.False(Find(state, "video.lock-aspect").IsChecked);
        Assert.True(Find(state, "audio.mute").IsChecked);
        Assert.True(Find(state, "playback.shuffle").IsChecked);
        Assert.True(Find(state, "playback.ab-loop").IsChecked);
        Assert.True(Find(state, "playback.loop-one").IsChecked);
        Assert.False(Find(state, "playback.loop-off").IsChecked);
        Assert.True(Find(state, "video.aspect.4:3").IsChecked);
        Assert.True(Find(state, "video.crop.5:4").IsChecked);
        Assert.True(Find(state, "video.rotate.90").IsChecked);
        Assert.True(Find(state, "video.flip-h").IsChecked);
        Assert.True(Find(state, "video.deinterlace").IsChecked);
    }

    [Fact]
    public void SpeedMenuIsGoneAndShortcutTextIsPresent()
    {
        var state = new MenuState { HasMedia = true, Speed = 2.0 };
        Assert.DoesNotContain(
            MenuCatalog.Flatten(MenuCatalog.Build(state)),
            item => item.Id == "playback.speed");
        Assert.Equal("Ctrl+,", Find(state, "window.settings").Shortcut);
        Assert.Equal("Ctrl+Shift+O", Find(state, "file.open-url").Shortcut);
        Assert.Equal("F11", Find(state, "video.fullscreen").Shortcut);
    }

    [Fact]
    public void DynamicSubmenusExistForTracksDevicesAndRecent()
    {
        var state = new MenuState
        {
            HasMedia = true,
            HasVideo = true,
            HasAudioTrack = true,
            HasSubtitleTrack = true,
        };
        Assert.Equal(MenuItemKind.Submenu, Find(state, "video.tracks").Kind);
        Assert.Equal(MenuItemKind.Submenu, Find(state, "audio.tracks").Kind);
        Assert.Equal(MenuItemKind.Submenu, Find(state, "subtitles.tracks").Kind);
        Assert.Equal(MenuItemKind.Submenu, Find(state, "subtitles.secondary").Kind);
        Assert.Equal(MenuItemKind.Submenu, Find(state, "audio.device").Kind);
        Assert.Equal(MenuItemKind.Submenu, Find(state, "file.recent").Kind);
        Assert.Equal(MenuItemKind.Submenu, Find(state, "video.aspect").Kind);
        Assert.NotEmpty(Find(state, "video.aspect").Children);
    }

    [Fact]
    public void EveryItemHasACommandOrChildren()
    {
        var items = MenuCatalog.Flatten(MenuCatalog.Build(new MenuState { HasMedia = true }));
        foreach (MenuItemDefinition item in items.Where(item => item.Kind != MenuItemKind.Separator))
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Command), item.Id);
            Assert.False(string.IsNullOrWhiteSpace(item.Label), item.Id);
        }
    }
}
