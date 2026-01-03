using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;
using OpenAI;
using System.ClientModel;
using OpenAI.Chat;
using Microsoft.Agents.AI;
using System.Text;

namespace WoofBot.Plugins.LLM;

public record LLMPluginConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public List<string> EnabledGroups { get; init; } = [];
    public string InstructionsFilePath { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
}

public class LLMPlugin : PluginBase<LLMPluginConfig>
{
    public LLMPlugin() : base("LLM", "1.0", "A simple LLM plugin") {}

    private ChatClientAgent? _agent;

    public override void Initialize()
    {
        base.Initialize();
        if (string.IsNullOrEmpty(Config.ApiKey)
            || string.IsNullOrEmpty(Config.Endpoint)
            || string.IsNullOrEmpty(Config.InstructionsFilePath)
            || string.IsNullOrEmpty(Config.ModelName))
        {
            Console.WriteLine("LLM Plugin is not properly configured. API key, Endpoint, InstructionsFilePath, or ModelName is missing.");
            return;
        }
        var instructions = File.ReadAllText(Config.InstructionsFilePath);
        OpenAIClient client = new(new ApiKeyCredential(Config.ApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(Config.Endpoint)
        });
        _agent = client
            .GetChatClient(Config.ModelName)
            .CreateAIAgent(
                instructions: instructions
            );
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (evt is MessageEvent msgEvt
            && msgEvt.Target.Type == TargetType.Group
            && Config.EnabledGroups.Contains(msgEvt.Target.Id)
            && msgEvt.Messages.Contains(new At(adapter.SelfId))
            && _agent is not null)
        {
            StringBuilder sb = new();
            foreach (var msg in msgEvt.Messages)
            {
                sb.Append(msg switch
                {
                    Text text => text.Content,
                    At at => at.Target == adapter.SelfId ? "@(法仆塔)" : $"@({at.Target})",
                    _ => string.Empty
                });
            }
            var userMessage = sb.ToString();
            Console.WriteLine("User message: " + userMessage);
            var response = await _agent.RunAsync(userMessage);

            Console.WriteLine(response);
            await adapter.SendMessageAsync(msgEvt.Target, [new Text(response.Text)]);

        }

    }
}