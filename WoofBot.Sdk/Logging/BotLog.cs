using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WoofBot.Sdk.Logging;

public static class BotLog
{
    public const string PluginCategoryPrefix = "Plugin.";

    private static ILoggerFactory s_loggerFactory = NullLoggerFactory.Instance;

    public static void Configure(ILoggerFactory loggerFactory)
    {
        s_loggerFactory = loggerFactory;
    }

    public static ILogger CreateLogger(string categoryName)
    {
        return s_loggerFactory.CreateLogger(categoryName);
    }

    public static ILogger<T> CreateLogger<T>()
    {
        return s_loggerFactory.CreateLogger<T>();
    }

    public static ILogger CreatePluginLogger(string pluginName)
    {
        return CreateLogger(PluginCategoryPrefix + NormalizePluginName(pluginName));
    }

    private static string NormalizePluginName(string pluginName)
    {
        return string.IsNullOrWhiteSpace(pluginName) ? "Unknown" : pluginName.Trim();
    }
}
