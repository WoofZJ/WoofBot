using System.Reflection;
using Microsoft.Extensions.Logging;
using WoofBot.Adapters.Milky;
using WoofBot.Core;
using WoofBot.Core.Logging;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Logging;
using WoofBot.Sdk.Serialization;

string binDir = AppDomain.CurrentDomain.BaseDirectory;
string rootDir = Path.Combine(binDir, "..");
string configDir = Path.Combine(rootDir, "configs");
string pluginsDir = Path.Combine(rootDir, "plugins");

LoggingRuntime loggingRuntime = CoreLogging.Configure(rootDir, new LoggingConfig());
ILogger logger = BotLog.CreateLogger("WoofBot.Core.Program");

try
{
    MilkyConfig milkyConfig = ConfigSerializer.LoadConfig<MilkyConfig>(
        Path.Combine(configDir, "core.json"),
        logger
    );

    var configuredLoggingRuntime = CoreLogging.Configure(rootDir, milkyConfig.Logging);
    loggingRuntime.Dispose();
    loggingRuntime = configuredLoggingRuntime;
    logger = BotLog.CreateLogger("WoofBot.Core.Program");

    logger.LogInformation("WoofBot starting. Root directory: {RootDir}", Path.GetFullPath(rootDir));

    MilkyAdapter milky = new(milkyConfig, BotLog.CreateLogger<MilkyAdapter>());
    await milky.StartAsync();

    using CronScheduler cronScheduler = new(BotLog.CreateLogger<CronScheduler>());

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
                            logger.LogInformation(
                                "Loaded plugin: {PluginName} {PluginVersion}",
                                plugin.Name,
                                plugin.Version
                            );
                            plugin.Initialize(configDir, cronScheduler);
                            plugin.Subscribe(milky);
                            plugin.Enable();
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to load plugin from {PluginPath}.", dll);
                }
            }
        }
    }
    else
    {
        logger.LogWarning("Plugins directory not found: {PluginsDir}", pluginsDir);
    }

    await Task.Delay(-1);
}
catch (Exception ex)
{
    logger.LogCritical(ex, "WoofBot terminated unexpectedly.");
    throw;
}
finally
{
    loggingRuntime.Dispose();
}
