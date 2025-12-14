namespace WoofBot.Adapters.OneBot.Models.Events;

public abstract record OneBotMetaEvent : OneBotEvent
{
    public string MetaEventType { get; init; } = string.Empty;
}

public record HeartbeatEvent : OneBotMetaEvent
{
    public int Interval { get; init; }
    public record HeartbeatStatus
    {
        public bool Online { get; init; }
        public bool Good { get; init; }
    }
    public HeartbeatStatus Status { get; init; } = new HeartbeatStatus();
}

public record LifecycleEvent : OneBotMetaEvent
{
    public string SubType { get; init; } = string.Empty;
}