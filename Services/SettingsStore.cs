using System.IO;
using System.Text.Json;
using EvNav.Models;

namespace EvNav.Services;

public sealed class AppSettings
{
    public string LastOsmFile { get; set; } = "";
    public VehicleProfile Vehicle { get; set; } = new();
    public string OrsApiKey { get; set; } = "";
    public string OcmApiKey { get; set; } = "";
    public bool UseOnline { get; set; }
}

public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EvNav");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(FilePath));
                if (s is not null) return s;
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(FilePath, json);
        }
        catch
        {
        }
    }
}
