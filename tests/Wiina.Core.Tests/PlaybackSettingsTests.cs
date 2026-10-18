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
                KeyBindingProfile = KeyBindingProfiles.Iina,
            };
            store.Save(saved);

            AppSettings loaded = new SettingsStore(path).Load();

            Assert.False(loaded.AutoLoadSmartSubtitles);
            Assert.Equal(SubtitleLanguagePreferences.TraditionalChinese, loaded.PreferredSubtitleLanguage);
            Assert.False(loaded.AutoLoadExternalAudio);
            Assert.Equal(KeyBindingProfiles.Iina, loaded.KeyBindingProfile);
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
