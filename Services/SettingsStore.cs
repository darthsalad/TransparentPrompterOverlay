using System.IO;
using System.Text.Json;
using TransparentPrompterOverlay.Models;

namespace TransparentPrompterOverlay.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(appDataRoot, "TransparentPrompterOverlay");
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new OverlaySettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<OverlaySettings>(json, JsonOptions) ?? new OverlaySettings();
        }
        catch
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
