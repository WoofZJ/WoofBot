namespace WoofBot.Sdk.Models;

public abstract record MessageSegment;

public record Text(string Content) : MessageSegment;

public record Image(string File) : MessageSegment;

public class Messages : List<MessageSegment>
{
}