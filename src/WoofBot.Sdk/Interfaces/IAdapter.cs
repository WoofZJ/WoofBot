using WoofBot.Sdk.Models;

namespace WoofBot.Sdk.Interfaces;

public interface IAdapter
{
    string Name { get; }

    Task StartAsync();
    Task StopAsync();
    Task SendMessageAsync(Target target, Messages messages);

    event Func<Event, IAdapter, Task> OnEventReceived;
}