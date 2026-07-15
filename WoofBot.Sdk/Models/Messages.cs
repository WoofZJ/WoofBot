using System.Text.Json.Serialization;

namespace WoofBot.Sdk.Models;

[JsonDerivedType(typeof(UnSupportedSegment), typeDiscriminator: "unsupported")]
[JsonDerivedType(typeof(Text), typeDiscriminator: "text")]
[JsonDerivedType(typeof(Image), typeDiscriminator: "image")]
[JsonDerivedType(typeof(ImageRecv), typeDiscriminator: "image_recv")]
[JsonDerivedType(typeof(At), typeDiscriminator: "at")]
[JsonDerivedType(typeof(Reply), typeDiscriminator: "reply")]
[JsonDerivedType(typeof(Face), typeDiscriminator: "face")]
[JsonDerivedType(typeof(Sticker), typeDiscriminator: "sticker")]
[JsonDerivedType(typeof(Video), typeDiscriminator: "video")]
public abstract record MessageSegment;

public record UnSupportedSegment : MessageSegment;

public record Text(string Content) : MessageSegment;

public record Image(string File) : MessageSegment;

public record Video(string File) : MessageSegment;

public record ImageRecv(string File, string Url, int? Width, int? Height, long? FileSize)
    : MessageSegment;

public record At(string Target) : MessageSegment;

public record Reply(long MessageId) : MessageSegment;

public record Face(int Id) : MessageSegment;

public record Sticker(int PackageId, string EmojiId, string Key) : MessageSegment;

public record LightApp(string AppId, string Title, string Description, string Url) : MessageSegment;

public record UploadFile(string Name, string Uri, string Folder) : MessageSegment;

public record GroupedMessagePiece(string UserId, string Name, Messages Messages);

public record GroupedMessage(
    string? Title,
    string[]? Preview,
    string? Summary,
    string? Prompt,
    List<GroupedMessagePiece> Pieces
) : MessageSegment;

public class Messages : List<MessageSegment> { }
