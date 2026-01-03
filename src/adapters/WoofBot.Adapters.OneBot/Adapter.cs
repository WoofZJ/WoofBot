using Fleck;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;
using WoofBot.Adapters.OneBot.Serializer;
using WoofBot.Adapters.OneBot.Models.Events;
using WoofBot.Adapters.OneBot.Models.Messages;
using WoofBot.Adapters.OneBot.Models.Apis;

namespace WoofBot.Adapters.OneBot;

public record OneBotConfig(
    string Host,
    int Port,
    string Suffix,
    string Token
);

public class OneBotAdapter(OneBotConfig config) : IAdapter
{
    public string Name => "OneBot";
    public string SelfId { get; set; } = string.Empty;

    public event Func<Event, IAdapter, Task>? OnEventReceived;

    private readonly OneBotConfig _config = config;
    private WebSocketServer? _server;
    private IWebSocketConnection? _socket;
    private readonly Dictionary<string, TaskCompletionSource<ApiData>> PendingApiCalls = [];

    public async Task StartAsync()
    {
        if (_server is not null) throw new InvalidOperationException("Adapter is already started.");
        _server = new WebSocketServer($"ws://{_config.Host}:{_config.Port}");
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                string suffix = socket.ConnectionInfo.Path;
                string token = socket.ConnectionInfo.Headers["Authorization"].Split(' ').Last();
                if (suffix != _config.Suffix || token != _config.Token)
                {
                    Console.WriteLine($"Failed authentication! Received suffix: {suffix}, token: {token}");
                    socket.Close();
                    return;
                }
                _socket = socket;
                socket.OnMessage = async (message) =>
                {
                    await HandleMessageAsync(message);
                };
            };
            socket.OnClose = () =>
            {
                Console.WriteLine("OneBot client disconnected.");
                _socket = null;
            };
        });
        while (_socket is null)
        {
            Console.WriteLine("Waiting for OneBot client connection...");
            await Task.Delay(2000);
        }
        var loginInfo = await CallApiAsync<GetLoginInfoPayload, GetLoginInfoData>("get_login_info", new());
        SelfId = loginInfo.UserId.ToString();
        Console.WriteLine($"OneBot adapter started. Self ID: {SelfId}");
    }

    private async Task HandleMessageAsync(string message)
    {
        Console.WriteLine("Received message: " + message);
        EventBase? evt = OneBotSerializer.Deserialize<EventBase>(message);
        if (evt is null)
        {
            Console.WriteLine("Failed to deserialize message.");
            return;
        }
        switch (evt)
        {
            case IApiEvent<ApiData> apiEvt:
                {
                    if (PendingApiCalls.TryGetValue(apiEvt.Echo, out TaskCompletionSource<ApiData>? tcs))
                    {
                        tcs.SetResult(apiEvt.Data);
                        PendingApiCalls.Remove(apiEvt.Echo);
                    }
                }
                break;
            case OneBotEvent onebotEvt:
                {
                    var woofEvent = onebotEvt.ToWoofBotEvent();
                    if (woofEvent is not null && OnEventReceived is not null)
                    {
                        await OnEventReceived(woofEvent, this);
                    }
                }
                break;
        }
    }

    public async Task<long> SendMessageAsync(Target target, Messages messages)
    {
        if (_socket is null) throw new InvalidOperationException("Socket is not connected.");
        MsgChain oneBotMessage = messages.ToOneBotMsgChain();
        switch (target.Type)
        {
            case TargetType.Group:
                return (await SendGroupMsgAsync(long.Parse(target.Id), oneBotMessage)).MessageId;
            case TargetType.Private:
                return (await SendPrivateMsgAsync(long.Parse(target.Id), oneBotMessage)).MessageId;
            default:
                throw new NotSupportedException("Target type not supported.");
        }
    }

    private async Task<TData> CallApiAsync<TPayload, TData>(string action, TPayload payload) where TPayload : ApiPayload where TData : ApiData
    {
        if (_socket == null) throw new InvalidOperationException("Socket is not connected.");
        var echo = $"{action}/{Guid.NewGuid():N}";
        PendingApiCalls[echo] = new TaskCompletionSource<ApiData>();
        await _socket.Send(OneBotSerializer.Serialize(new
        {
            action,
            @params = payload,
            echo
        }));
        return (TData)await PendingApiCalls[echo].Task;
    }

    private async Task<SendGroupMsgData> SendGroupMsgAsync(long groupId, MsgChain message) =>
        await CallApiAsync<SendGroupMsgPayload, SendGroupMsgData>("send_group_msg", new(groupId, message));

    private async Task<SendPrivateMsgData> SendPrivateMsgAsync(long userId, MsgChain message) =>
        await CallApiAsync<SendPrivateMsgPayload, SendPrivateMsgData>("send_private_msg", new(userId, message));

    public Task StopAsync()
    {
        _socket?.Close();
        _server?.Dispose();
        _socket = null;
        _server = null;
        return Task.CompletedTask;
    }
}
