using Fleck;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;

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

    public event Func<Event, Task> OnEventReceived;

    private readonly OneBotConfig _config = config;
    private WebSocketServer? _server;
    private IWebSocketConnection? _socket;

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
                socket.OnMessage = (message) =>
                {
                    Console.WriteLine($"Received message: {message}");
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
    }

    public Task SendMessageAsync(Target target, Messages messages)
    {
        throw new NotImplementedException();
    }

    public Task StopAsync()
    {
        _socket?.Close();
        _server?.Dispose();
        _socket = null;
        _server = null;
        return Task.CompletedTask;
    }
}
