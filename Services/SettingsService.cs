using System.IO;
using System.Text.Json;

namespace DisplayControl.Services;

public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DisplayControl",
        "settings.json"
    );

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
        }
        catch
        {
            Settings = new();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Silently fail — settings aren't critical
        }
    }
}

public class AppSettings
{
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeOnClose { get; set; } = false;
    public bool ResetOnExit { get; set; } = false;
    public bool RealTimeUpdates { get; set;  } = false;
    public bool RealTimeHistogram { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = false;

}