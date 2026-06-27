using System.Text.Json;

namespace MouseDebouncer;

/// <summary>
/// 버튼별 디바운스 딜레이(ms) 설정. 0이면 해당 버튼 비활성화.
/// </summary>
public class AppConfig
{
    public int Left     { get; set; } = 0;
    public int Right    { get; set; } = 0;
    public int Middle   { get; set; } = 0;
    public int XButton1 { get; set; } = 300;
    public int XButton2 { get; set; } = 300;
}

/// <summary>
/// config.json 읽기/쓰기 담당
/// </summary>
public static class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            string json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(ConfigPath, json);
    }
}
