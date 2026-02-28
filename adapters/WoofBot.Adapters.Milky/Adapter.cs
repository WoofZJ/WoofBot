using Milky.Net.Client;
using Milky.Net.Model;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;
using Event = WoofBot.Sdk.Models.Event;

namespace WoofBot.Adapters.Milky;

public record MilkyConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Token { get; set; } = "";
}

public class MilkyAdapter(MilkyConfig config) : IAdapter
{
    public string Name => "Milky";
    public string SelfId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;

    public event Func<Event, IAdapter, Task>? OnEventReceived;

    private readonly MilkyConfig _config = config;
    private MilkyClient? _client;
    private readonly CancellationTokenSource _cts = new();

    public async Task StartAsync()
    {
        if (_client is not null)
            throw new InvalidOperationException("Adapter is already started.");
        HttpClient httpClient = new()
        {
            BaseAddress = new Uri($"ws://{_config.Host}:{_config.Port}"),
            DefaultRequestHeaders = { Authorization = new("Bearer", _config.Token) },
        };
        _client = new MilkyClient(httpClient);
        var response = await _client.System.GetLoginInfoAsync();
        SelfId = response.Uin.ToString();
        Nickname = response.Nickname;
        _client.Events.MessageReceive += async (milky, evt) =>
        {
            Event? woofEvent = evt.ToWoofBotEvent();
            if (woofEvent is not null && OnEventReceived is not null)
            {
                await OnEventReceived.Invoke(woofEvent, this);
            }
        };
        _ = _client.ReceivingEventUsingWebSocketAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_client is not null)
        {
            _cts.Cancel();
            _client = null;
        }
    }

    public async Task<long> SendMessageAsync(Target target, Messages messages)
    {
        if (_client is null)
            throw new InvalidOperationException("Adapter is not started.");
        OutgoingSegment[] segments = messages.ToMilkySegments();
        switch (target.Type)
        {
            case TargetType.Private:
                var privateMsgResponse = await _client.Message.SendPrivateMessageAsync(
                    new(long.Parse(target.Id), segments)
                );
                return privateMsgResponse.MessageSeq;
            case TargetType.Group:
                var groupMsgResponse = await _client.Message.SendGroupMessageAsync(
                    new(long.Parse(target.Id), segments)
                );
                return groupMsgResponse.MessageSeq;
            default:
                throw new NotSupportedException($"Unsupported target type: {target.Type}");
        }
    }
}
