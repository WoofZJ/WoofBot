using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using WoofBot.Sdk.Logging;

namespace WoofBot.Core.Logging;

internal static class CoreLogging
{
    private const string CoreLogFileName = "core.log";
    private const string PluginNamePropertyName = "PluginName";
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    public static LoggingRuntime Configure(string rootDir, LoggingConfig config)
    {
        string logDir = ResolveLogDirectory(rootDir, config.Directory);
        Directory.CreateDirectory(logDir);

        var minimumLevel = ParseMinimumLevel(config.Level);
        var fileSizeLimitBytes = Math.Max(config.FileSizeLimitBytes, 1024 * 1024);
        int? retainedFileCountLimit =
            config.RetainedFileCountLimit <= 0 ? null : config.RetainedFileCountLimit;

        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .Enrich.With(new PluginNameEnricher())
            .WriteTo.Logger(lc =>
                lc.Filter.ByExcluding(e => e.Properties.ContainsKey(PluginNamePropertyName))
                    .WriteTo.File(
                        Path.Combine(logDir, CoreLogFileName),
                        outputTemplate: OutputTemplate,
                        fileSizeLimitBytes: fileSizeLimitBytes,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: retainedFileCountLimit,
                        shared: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(1)
                    )
            )
            .WriteTo.Logger(lc =>
                lc.Filter.ByIncludingOnly(e => e.Properties.ContainsKey(PluginNamePropertyName))
                    .WriteTo.Map(
                        PluginNamePropertyName,
                        "unknown",
                        (pluginName, wt) =>
                            wt.File(
                                Path.Combine(logDir, $"{SanitizeFileName(pluginName)}.log"),
                                outputTemplate: OutputTemplate,
                                fileSizeLimitBytes: fileSizeLimitBytes,
                                rollOnFileSizeLimit: true,
                                retainedFileCountLimit: retainedFileCountLimit,
                                shared: true,
                                flushToDiskInterval: TimeSpan.FromSeconds(1)
                            )
                    )
            );

        if (config.WriteToConsole)
        {
            loggerConfiguration.WriteTo.Console(outputTemplate: OutputTemplate);
        }

        var serilogLogger = loggerConfiguration.CreateLogger();
        Log.Logger = serilogLogger;

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(serilogLogger, dispose: true);
        });
        BotLog.Configure(loggerFactory);

        return new LoggingRuntime(loggerFactory, logDir);
    }

    private static string ResolveLogDirectory(string rootDir, string configuredDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
            configuredDirectory = "logs";

        return Path.GetFullPath(
            Path.IsPathRooted(configuredDirectory)
                ? configuredDirectory
                : Path.Combine(rootDir, configuredDirectory)
        );
    }

    private static LogEventLevel ParseMinimumLevel(string level)
    {
        return Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : LogEventLevel.Information;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value
            .Trim()
            .Select(ch => invalidChars.Contains(ch) ? '_' : char.ToLowerInvariant(ch))
            .ToArray();
        var sanitized = new string(chars).Trim('.', '_');

        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private sealed class PluginNameEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            if (
                !logEvent.Properties.TryGetValue(
                    global::Serilog.Core.Constants.SourceContextPropertyName,
                    out var sourceContextValue
                )
                || sourceContextValue is not ScalarValue { Value: string sourceContext }
            )
                return;

            if (
                !sourceContext.StartsWith(
                    BotLog.PluginCategoryPrefix,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return;

            string pluginName = sourceContext[BotLog.PluginCategoryPrefix.Length..];
            int subCategoryIndex = pluginName.IndexOf('.');
            if (subCategoryIndex >= 0)
                pluginName = pluginName[..subCategoryIndex];

            pluginName = string.IsNullOrWhiteSpace(pluginName) ? "unknown" : pluginName;
            logEvent.AddPropertyIfAbsent(
                propertyFactory.CreateProperty(PluginNamePropertyName, pluginName)
            );
        }
    }
}

internal sealed class LoggingRuntime(ILoggerFactory loggerFactory, string logDirectory) : IDisposable
{
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public string LogDirectory { get; } = logDirectory;

    public void Dispose()
    {
        LoggerFactory.Dispose();
    }
}
