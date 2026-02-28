using System.ComponentModel.DataAnnotations;
using WoofBot.Adapters.OneBot.Models.Messages;

namespace WoofBot.Adapters.OneBot.Models.Events;

public abstract record OneBotMessageEvent : OneBotEvent
{
    public string MessageType { get; init; } = string.Empty;
}

public record PrivateMessageEvent : OneBotMessageEvent
{
    [AllowedValues("friend", "group", "other")]
    public string SubType { get; init; } = string.Empty;
    public long MessageId { get; init; }
    public long UserId { get; init; }
    public long MessageSeq { get; init; }
    public MsgChain Message { get; init; } = [];
    public string RawMessage { get; init; } = string.Empty;
    public int Font { get; init; }
    public record SenderInfo
    {
        public long UserId { get; init; }
        public string Nickname { get; init; } = string.Empty;
        public string Card { get; init; } = string.Empty;
    }
    public SenderInfo Sender { get; init; } = new SenderInfo();
}

public record GroupMessageEvent : OneBotMessageEvent
{
    [AllowedValues("normal", "anonymous", "notice")]
    public string SubType { get; init; } = string.Empty;
    public long MessageId { get; init; }
    public long MessageSeq { get; init; }
    public long GroupId { get; init; }
    public long UserId { get; init; }
    public MsgChain Message { get; init; } = [];
    public string GroupName { get; init; } = string.Empty;
    public string RawMessage { get; init; } = string.Empty;
    public int Font { get; init; }
    public record SenderInfo
    {
        public long UserId { get; init; }
        public string Nickname { get; init; } = string.Empty;
        public string Card { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
    }
    public SenderInfo Sender { get; init; } = new SenderInfo();
}