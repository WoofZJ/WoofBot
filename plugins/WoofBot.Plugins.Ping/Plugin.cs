using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;

namespace WoofBot.Plugins.Ping;

public record PingPluginConfig
{
    public List<string> Admins { get; init; } = [];
}

public class PingPlugin : PluginBase<PingPluginConfig>
{
    public PingPlugin() : base("Ping", "1.0", "A simple ping plugin") {}

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (evt is MessageEvent msgEvt
            && msgEvt.Target.Type == TargetType.Group
            && Config.Admins.Contains(msgEvt.SenderId)
            && msgEvt.Messages is [Text("ping")])
        {
            await adapter.SendMessageAsync(msgEvt.Target, [new Text("pong")]);
        }
    }
}
