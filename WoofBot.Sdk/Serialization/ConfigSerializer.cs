using System.Text.Json;

namespace WoofBot.Sdk.Serialization;

public static class ConfigSerializer
{
    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static T LoadConfig<T>(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[Warning] Config file not found: {path}");
            Console.WriteLine("[Info] Creating default config...");
            string dir = Path.GetDirectoryName(path) ?? "";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            T defaultConfig = Activator.CreateInstance<T>();
            SaveConfig(path, defaultConfig);
            return default!;
        }
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, s_readOptions)!;
    }

    public static void SaveConfig<T>(string path, T config)
    {
        string json = JsonSerializer.Serialize(config, s_writeOptions);
        File.WriteAllText(path, json);
    }
}
