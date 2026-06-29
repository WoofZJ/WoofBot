using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoofBot.Plugins.Rocom;

public record Pokemon
{
    [JsonPropertyName("t_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int TId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Attributes { get; init; } = string.Empty;
    public string ChainGroup { get; init; } = string.Empty;
    public string EggDiameter { get; init; } = string.Empty;
    public string EggWeight { get; init; } = string.Empty;
    public int EvolutionStage { get; init; }
    public bool IsBig { get; init; }
    public bool IsTiny { get; init; }
    public float Prob { get; init; }
}

public record EggQueryResult
{
    public int Count { get; init; }
    public List<Pokemon> Pokemons { get; init; } = [];
    bool Success { get; init; }
    int TotalMatches { get; init; }
    string Message { get; init; } = string.Empty;
}

public class CustomDateTimeConverter : JsonConverter<DateTime>
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var dateString = reader.GetString();
        if (
            DateTime.TryParseExact(
                dateString,
                DateFormat,
                null,
                System.Globalization.DateTimeStyles.None,
                out var date
            )
        )
        {
            return date;
        }
        throw new JsonException($"Invalid date format. Expected format: {DateFormat}");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString(DateFormat));
    }
}

public record MerchantItem
{
    public string Category { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long Price { get; init; }
    public string PriceRaw { get; init; } = string.Empty;
    public long Limit { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
}

public record MerchantResult
{
    public List<MerchantItem> Items { get; init; } = [];
    public string Status { get; init; } = string.Empty;
    public bool Live { get; init; }

    [JsonConverter(typeof(CustomDateTimeConverter))]
    public DateTime StartedAtBeijing { get; init; }

    [JsonConverter(typeof(CustomDateTimeConverter))]
    public DateTime NextRefreshBeijing { get; init; }
    public int DurationHours { get; init; }
    public int Round { get; init; }
}
