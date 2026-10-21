using System;
using System.IO;
using System.Text.Json;

namespace MCLauncher;

public class AppSettings
{
    public int MaxThreads { get; set; } = 16;
    public int MaxMemoryMB { get; set; } = 4096; // 默认 4G 内存
    public string LastUsername { get; set; } = "Steve";
    public string LastVersion { get; set; } = "";

    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
