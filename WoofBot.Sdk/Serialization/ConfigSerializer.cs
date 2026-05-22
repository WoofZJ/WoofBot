using System.Text.Json;
using Microsoft.Extensions.Logging;
using WoofBot.Sdk.Logging;

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

    public static T LoadConfig<T>(string path, ILogger? logger = null)
    {
        logger ??= BotLog.CreateLogger(typeof(ConfigSerializer).FullName ?? nameof(ConfigSerializer));

        if (!File.Exists(path))
        {
            logger.LogWarning("Config file not found: {Path}", path);
            logger.LogInformation("Creating default config at {Path}", path);
            string dir = Path.GetDirectoryName(path) ?? "";
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            T defaultConfig = Activator.CreateInstance<T>();
            SaveConfig(path, defaultConfig);
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
