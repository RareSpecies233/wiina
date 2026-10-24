using System;
using System.IO;
using Wiina.Core.Input;
using Wiina.Core.Settings;
using Wiina.Core.Subtitles;
using Xunit;

namespace Wiina.Core.Tests;

public sealed class PlaybackSettingsTests
{
    [Fact]
    public void NewPlaybackSettingsHaveExpectedDefaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.AutoLoadSmartSubtitles);
        Assert.Equal(SubtitleLanguagePreferences.Auto, settings.PreferredSubtitleLanguage);
        Assert.True(settings.AutoLoadExternalAudio);
        Assert.Equal(KeyBindingProfiles.Default, settings.KeyBindingProfile);
        Assert.Equal(AutoPlaylistMode.SameFolder, settings.AutoPlaylistMode);
    }

    [Fact]
    public void PlaybackSettingsRoundTripThroughStore()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "wiina-settings-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        try
        {
            var store = new SettingsStore(path);
            var saved = new AppSettings
            {
                AutoLoadSmartSubtitles = false,
                PreferredSubtitleLanguage = SubtitleLanguagePreferences.TraditionalChinese,
                AutoLoadExternalAudio = false,
                KeyBindingProfile = KeyBindingProfiles.Mpv,
                AutoPlaylistMode = AutoPlaylistMode.IncludeSubfolders,
            };
            store.Save(saved);

            AppSettings loaded = new SettingsStore(path).Load();

            Assert.False(loaded.AutoLoadSmartSubtitles);
            Assert.Equal(SubtitleLanguagePreferences.TraditionalChinese, loaded.PreferredSubtitleLanguage);
            Assert.False(loaded.AutoLoadExternalAudio);
            Assert.Equal(KeyBindingProfiles.Mpv, loaded.KeyBindingProfile);
            Assert.Equal(AutoPlaylistMode.IncludeSubfolders, loaded.AutoPlaylistMode);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort for the test sandbox.
            }
        }
    }

    [Fact]
    public void LegacyIinaProfileInSettingsJsonMigratesToWiinaOnLoad()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "wiina-settings-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");

        try
        {
            // Raw JSON as written by builds before 1.3.1.
            File.WriteAllText(path, "{\n  \"KeyBindingProfile\": \"IINA\"\n}\n");

            AppSettings loaded = new SettingsStore(path).Load();

            Assert.Equal(KeyBindingProfiles.Wiina, loaded.KeyBindingProfile);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort for the test sandbox.
            }
        }
    }
}
