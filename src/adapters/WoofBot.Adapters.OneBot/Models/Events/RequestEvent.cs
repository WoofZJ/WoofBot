using System.ComponentModel.DataAnnotations;

namespace WoofBot.Adapters.OneBot.Models.Events;

public abstract record OneBotRequestEvent : OneBotEvent
{
    public string RequestType { get; init; } = string.Empty;
}

public record FriendRequestEvent : OneBotRequestEvent
{
    public long UserId { get; init; }
    public string Comment { get; init; } = string.Empty;
    public string Flag { get; init; } = string.Empty;
}

public record GroupRequestEvent : OneBotRequestEvent
{
    [AllowedValues("add", "invite")]
    public string SubType { get; init; } = string.Empty;
    public long GroupId { get; init; }
    public long UserId { get; init; }
    public string Comment { get; init; } = string.Empty;
    public string Flag { get; init; } = string.Empty;
}