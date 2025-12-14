using System.Text.Json.Serialization;

namespace WoofBot.Adapters.OneBot.Models.Messages;

public abstract record MsgSegment;

public record PlainText(
    string Text
) : MsgSegment;

public record Image(
    string File
) : MsgSegment;

public record ImageRecv : MsgSegment
{
    public string File { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public long FileSize { get; init; }
    [JsonPropertyName("subType")]
    public int SubType { get; init; }
}

public record Face(
    int Id,
    int? SubType = null
) : MsgSegment;

public record At(
    long Qq,
    string? Name = null
) : MsgSegment;

public record AtAll(string Qq = "all") : MsgSegment;

public record Reply(
    long Id
) : MsgSegment;

public record MarketFace(
    int EmojiPackageId,
    string EmojiId,
    string Key,
    string? Summary = null,
    string? Url = null
) : MsgSegment;