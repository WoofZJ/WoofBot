using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;

namespace WoofBot.Plugins.LLM;

public record LLMPluginConfig { }

public class LLMPlugin : PluginBase<LLMPluginConfig>
{
    public LLMPlugin()
        : base("LLM", "1.0", "A simple LLM plugin") { }

    public override void Initialize(string configDir, ICronScheduler cronScheduler)
    {
        base.Initialize(configDir, cronScheduler);
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter) { }
}
