using Cronos;

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

public record CronEvent : Event
{
    public string TaskName { get; init; } = string.Empty;
    public CronExpression Cron { get; init; } = default!;
    public DateTimeOffset? CurrentOccurrence { get; init; }
    public int OccurrenceCount { get; init; }
    public int MaxOccurrences { get; init; }
}
