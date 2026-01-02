using System.Reflection;
using System.Text.Json;
using WoofBot.Sdk.Interfaces;
using WoofBot.Adapters.OneBot;

string json = File.ReadAllText("configs/core.json");
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
};
var coreConfig = JsonSerializer.Deserialize<OneBotConfig>(json, options);

if (coreConfig is null)
{
    Console.WriteLine("[Error] Failed to load core configuration.");
    return;
}

OneBotAdapter onebot = new(coreConfig);

var pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
if (Directory.Exists(pluginsDir))
{
    foreach (var dir in Directory.GetDirectories(pluginsDir))
    {
        foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin)
                    {
                        Console.WriteLine($"[System] Loaded plugin: {plugin.Name} {plugin.Version}");
                        plugin.Initialize();
                        plugin.Subscribe(onebot);
                        plugin.Enable();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to load plugin from {dll}: {ex.Message}");
            }
        }
    }
}

await onebot.StartAsync();
await Task.Delay(-1);
await onebot.StopAsync();
