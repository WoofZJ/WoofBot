namespace WoofBot.Sdk.Logging;

public record LoggingConfig
{
    public string Level { get; init; } = "Information";
    public string Directory { get; init; } = "logs";
    public long FileSizeLimitBytes { get; init; } = 10 * 1024 * 1024;
    public int RetainedFileCountLimit { get; init; } = 10;
    public bool WriteToConsole { get; init; } = true;
}
