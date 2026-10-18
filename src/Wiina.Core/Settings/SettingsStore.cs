using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wiina.Core.Settings;

/// <summary>Loads/saves <see cref="AppSettings"/> atomically.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Wiina",
            "settings.json");
    }

    public string Path { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                string json = File.ReadAllText(Path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                    ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt settings must never prevent startup.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }
}
