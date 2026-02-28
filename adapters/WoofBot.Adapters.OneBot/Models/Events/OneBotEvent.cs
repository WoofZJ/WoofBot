using WoofBot.Adapters.OneBot.Models.Apis;

namespace WoofBot.Adapters.OneBot.Models.Events;

public abstract record EventBase;

public abstract record OneBotEvent : EventBase
{
    public long Time { get; init; }
    public long SelfId { get; init; }
    public string PostType { get; init; } = string.Empty;
};

public interface IApiEvent<out TData> where TData : ApiData
{
    string Echo { get; }
    TData Data { get; }
}

public record ApiEvent<TData> : EventBase, IApiEvent<TData> where TData : ApiData
{
    public string Echo { get; init; } = string.Empty;
    public TData Data { get; init; } = default!;
}