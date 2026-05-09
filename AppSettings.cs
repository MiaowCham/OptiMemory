using System.Text.Json;

namespace OptiMemory;

public sealed class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OptiMemory", "settings.json");

    public bool AutoClean { get; set; } = false;
    public int AutoCleanIntervalMinutes { get; set; } = 30;
    /// <summary>只有内存占用百分比超过此阈值时才触发自动清理（0 = 始终触发）</summary>
    public int AutoCleanThresholdPercent { get; set; } = 70;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { /* ignore, use defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* ignore */ }
    }
}
