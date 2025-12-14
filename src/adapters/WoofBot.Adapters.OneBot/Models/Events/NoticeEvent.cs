using System.ComponentModel.DataAnnotations;

namespace WoofBot.Adapters.OneBot.Models.Events;

public abstract record OneBotNoticeEvent : OneBotEvent
{
    public string NoticeType { get; init; } = string.Empty;
}

public record GroupUploadEvent : OneBotNoticeEvent
{
    public long GroupId { get; init; }
    public long UserId { get; init; }
    public record FileInfo
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public long Size { get; init; }
        public long BusId { get; init; }
    }
    public FileInfo File { get; init; } = new FileInfo();
}

public record GroupAdminEvent : OneBotNoticeEvent
{
    [AllowedValues("set", "unset")]
    public string SubType { get; init; } = string.Empty;
    public long GroupId { get; init; }
    public long UserId { get; init; }
}

public record GroupDecreaseEvent : OneBotNoticeEvent
{
    [AllowedValues("leave", "kick", "kick_me")]
    public string SubType { get; init; } = string.Empty;
    public long GroupId { get; init; }
    public long OperatorId { get; init; }
    public long UserId { get; init; }
}

public record GroupIncreaseEvent : OneBotNoticeEvent
{
    [AllowedValues("approve", "invite")]
    public string SubType { get; init; } = string.Empty;
    public long GroupId { get; init; }
    public long OperatorId { get; init; }
    public long UserId { get; init; }
}

public record GroupBanEvent : OneBotNoticeEvent
{
    [AllowedValues("ban", "lift_ban")]
    public string SubType { get; init; } = string.Empty;
    public long GroupId { get; init; }
    public long OperatorId { get; init; }
    public long UserId { get; init; }
    public long Duration { get; init; }
}

public record FriendAddEvent : OneBotNoticeEvent
{
    public long UserId { get; init; }
}

public record GroupRecallEvent : OneBotNoticeEvent
{
    public long GroupId { get; init; }
    public long UserId { get; init; }
    public long OperatorId { get; init; }
    public long MessageId { get; init; }
}

public record FriendRecallEvent : OneBotNoticeEvent
{
    public long UserId { get; init; }
    public long MessageId { get; init; }
}

public record NotifyEvent : OneBotNoticeEvent
{
    public string SubType { get; init; } = string.Empty;
}

public record PokeEvent : NotifyEvent
{
    public long GroupId { get; init; }
    public long UserId { get; init; }
    public long TargetId { get; init; }
}

public record LuckyKingEvent : NotifyEvent
{
    public long GroupId { get; init; }
    public long UserId { get; init; }
    public long TargetId { get; init; }
}

public record HonorEvent : NotifyEvent
{
    public long GroupId { get; init; }
    public long UserId { get; init; }
    [AllowedValues("talkative", "performer", "emotion")]
    public string HonorType { get; init; } = string.Empty;
}