namespace WoofBot.Sdk.Interfaces;

public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Description { get; }

    void Initialize();
    void Subscribe(IAdapter adapter);
    void Enable();
    void Disable();
    void Dispose();
}