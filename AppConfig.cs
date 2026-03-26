using System.Text.Json;

namespace SubLingo;

public class AppConfig
{
    public string SrtPath { get; set; } = "";
    public int VlcPort { get; set; } = 8080;
    public string VlcPassword { get; set; } = "";
    public string TargetLang { get; set; } = "tr";
    public string DeepLKey { get; set; } = "";

    public bool IsReady => !string.IsNullOrEmpty(SrtPath) && File.Exists(SrtPath);

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SubLingo", "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath))
                       ?? new AppConfig();
        }
        catch { }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
