using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using WoofBot.Adapters.OneBot.Models.Messages;

namespace WoofBot.Adapters.OneBot.Serializer;

public class MsgSegmentJsonConverter : JsonConverter<MsgSegment>
{
    public override MsgSegment? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader)!;
        string type = node["type"]!.GetValue<string>();
        var data = node["data"]!;
        if (data["type"] == null)
        {
            data["type"] = type;
            return type switch
            {
                "text" => data.Deserialize<PlainText>(options),
                "image" => data.Deserialize<ImageRecv>(options),
                "face" => data.Deserialize<Face>(options),
                "at" => data["qq"]?.GetValue<string>() == "all" ? new AtAll() : data.Deserialize<At>(options),
                "mface" => data.Deserialize<MarketFace>(options),
                "reply" => data.Deserialize<Reply>(options),
                _ => new UnknownMsgSegment(type, data),
            };
        }
        throw new NotSupportedException($"Unsupported MsgSegment type");
    }

    public override void Write(Utf8JsonWriter writer, MsgSegment value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value switch
        {
            PlainText => "text",
            Image => "image",
            Face => "face",
            At => "at",
            AtAll => "at",
            MarketFace => "mface",
            _ => throw new NotSupportedException($"Unsupported MsgSegment type")
        });
        writer.WritePropertyName("data");
        switch (value)
        {
            case At at:
                {
                    writer.WriteStartObject();
                    writer.WriteString("qq", at.Qq.ToString());
                    if (at.Name != null)
                    {
                        writer.WriteString("name", at.Name);
                    }
                    writer.WriteEndObject();
                    break;
                }
            default:
                {
                    JsonSerializer.Serialize(writer, value, value.GetType(), options);
                    break;
                }
        }
        writer.WriteEndObject();
    }
}