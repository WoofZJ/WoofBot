namespace WoofBot.Sdk.Models;

public abstract record Event
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public record MessageEvent : Event
{
    public Target Target { get; init; } = default!;
    public string SenderId { get; init; } = string.Empty;
    public Messages Messages { get; init; } = [];
}

public record NotifyEvent : Event;

public record CronEvent : Event;