using System.Text.Json;
using WoofBot.Sdk.Interfaces;

namespace WoofBot.Sdk.Models;

public abstract class PluginBase<TConfig>(string Name, string Version, string Description)
: IPlugin where TConfig : new()
{
    public virtual string Name { get; } = Name;
    public virtual string Version { get; } = Version;
    public virtual string Description { get; } = Description;

    protected List<IAdapter> Adapters { get; private set; } = [];
    protected bool IsEnabled { get; private set; } = false;
    protected TConfig Config { get; private set; } = new ();

    public virtual void LoadConfig()
    {
        var configPath = $"configs/{Name.ToLower()}.json";
        if (File.Exists(configPath))
        {
            try
            {
                string json = File.ReadAllText(configPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                };
                Config = JsonSerializer.Deserialize<TConfig>(json, options) ?? new TConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to load config for {Name}: {ex.Message}");
            }
        }
        else
        {
            Config = new TConfig();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            string json = JsonSerializer.Serialize(Config, options);
            File.WriteAllText(configPath, json);
        }
    }

    public virtual void Initialize()
    {
        LoadConfig();
    }

    public virtual void Subscribe(IAdapter adapter)
    {
        if (!Adapters.Contains(adapter))
        {
            adapter.OnEventReceived += async (evt, _) =>
            {
                if (!IsEnabled) return;
                try
                {
                    await HandleEventAsync(evt, adapter);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Exception in plugin {Name} handling event: {ex.Message}");
                }
            };
            Adapters.Add(adapter);
            Console.WriteLine($"[System] Plugin {Name} subscribed to adapter {adapter.Name}");
        }
    }

    protected abstract Task HandleEventAsync(Event evt, IAdapter adapter);

    public virtual void Enable()
    {
        IsEnabled = true;
    }

    public virtual void Disable()
    {
        IsEnabled = false;
    }

    public virtual void Dispose()
    {
        IsEnabled = false;
        Adapters.Clear();
    }
}