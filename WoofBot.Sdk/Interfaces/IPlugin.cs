namespace WoofBot.Sdk.Interfaces;

public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Description { get; }

    void Initialize(string configDir, ICronScheduler cronScheduler);
    void Subscribe(IAdapter adapter);
    void Enable();
    void Disable();
    void Dispose();
}
