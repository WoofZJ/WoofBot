using System.Reflection;
using WoofBot.Adapters.Milky;
using WoofBot.Core;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Serialization;

string binDir = AppDomain.CurrentDomain.BaseDirectory;
string rootDir = Path.Combine(binDir, "..");
string configDir = Path.Combine(rootDir, "configs");
string pluginsDir = Path.Combine(rootDir, "plugins");

MilkyConfig milkyConfig = ConfigSerializer.LoadConfig<MilkyConfig>(
    Path.Combine(configDir, "core.json")
);

MilkyAdapter milky = new(milkyConfig);
await milky.StartAsync();

using CronScheduler cronScheduler = new();

if (Directory.Exists(pluginsDir))
{
    foreach (var dir in Directory.GetDirectories(pluginsDir))
    {
        foreach (var dll in Directory.GetFiles(dir, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var pluginTypes = assembly
                    .GetTypes()
                    .Where(t =>
                        typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract
                    );
                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IPlugin plugin)
                    {
                        Console.WriteLine(
                            $"[System] Loaded plugin: {plugin.Name} {plugin.Version}"
                        );
                        plugin.Initialize(configDir, cronScheduler);
                        plugin.Subscribe(milky);
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

await Task.Delay(-1);
