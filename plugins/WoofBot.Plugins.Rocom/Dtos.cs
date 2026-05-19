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
