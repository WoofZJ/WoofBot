namespace WoofBot.Sdk.Models;

public abstract record MessageSegment;

public record UnSupportedSegment : MessageSegment;

public record Text(string Content) : MessageSegment;

public record Image(string File) : MessageSegment;

public record ImageRecv(
    string File,
    string Url,
    long FileSize
) : MessageSegment;

public record At(
    string Target
) : MessageSegment;

public record Reply(
    long MessageId
) : MessageSegment;

public class Messages : List<MessageSegment>
{
}