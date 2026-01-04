using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;
using OpenAI;
using System.ClientModel;
using OpenAI.Chat;
using Microsoft.Agents.AI;
using System.Text;
using System.ComponentModel;
using Microsoft.Extensions.AI;
using System.Text.Json;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace WoofBot.Plugins.LLM;

public record LLMPluginConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public List<string> EnabledGroups { get; init; } = [];
    public string InstructionsFilePath { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public List<string> WakeWords { get; init; } = [];
}

[Description("Response format that you should follow.")]
public record LLMResponse
{
    [Description("Whether a response is needed.")]
    public bool NeedResponse { get; init; } = false;
    [Description("The text content to be sent in response.")]
    public string ChatText { get; init; } = string.Empty;
    [Description("Whether to end the current session.")]
    public bool EndSession { get; init; } = false;
}

public class LLMPlugin : PluginBase<LLMPluginConfig>
{
    public LLMPlugin() : base("LLM", "1.0", "A simple LLM plugin") {}

    private ChatClientAgent? _agent;
    private Dictionary<string, AgentThread> _agentThreads = [];

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
                new ChatClientAgentOptions()
                {
                    Name = "WoofBot LLM Agent",
                    ChatOptions = new()
                    {
                        Instructions = instructions,
                        ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<LLMResponse>(),
                        Tools = [AIFunctionFactory.Create(GetCurrentDateTime)]
                    }
                }
            );
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (evt is not MessageEvent || _agent is null) return;
        var msgEvt = (MessageEvent)evt;
        if (msgEvt.Target.Type is not TargetType.Group
            || !Config.EnabledGroups.Contains(msgEvt.Target.Id))
            return;
        if (msgEvt.Messages.Contains(new At(adapter.SelfId))
            || msgEvt.Messages.Any(m => m is Text text && Config.WakeWords.Any(ww => text.Content.Contains(ww))))
        {
            if (!_agentThreads.ContainsKey(msgEvt.Target.Id))
            {
                _agentThreads[msgEvt.Target.Id] = _agent.GetNewThread();
            }
        }
        if (_agentThreads.TryGetValue(msgEvt.Target.Id, out AgentThread? thread))
        {
            StringBuilder sb = new($"<{msgEvt.Timestamp}> User({msgEvt.SenderId}): ");
            List<string> images = [];
            foreach (var msg in msgEvt.Messages)
            {
                switch (msg)
                {
                    case Text text:
                        sb.Append(text.Content);
                        break;
                    case At at:
                        sb.Append(at.Target == adapter.SelfId ? "@(我)" : $"@({at.Target})");
                        break;
                    case ImageRecv img:
                        sb.Append($"[图片,filename={img.File}]");
                        if (img.FileSize <= 4 * 1024 * 1024)
                        {
                            // var imgBytes = await GetImageFromUrl(img.Url);
                            // images.Add(imgBytes);
                            images.Add(img.Url);
                        }
                        break;
                }
            }
            List<AIContent> contents = [new TextContent(sb.ToString())];
            foreach (var imgUrl in images)
            {
                contents.Add(new UriContent(imgUrl, "image/jpeg"));
            }
            ChatMessage userMessage = new (ChatRole.User, contents);
            Console.WriteLine(userMessage);
            var response = await _agent.RunAsync<LLMResponse>(userMessage, thread);
            var llmResponse = response.Deserialize<LLMResponse>(JsonSerializerOptions.Web);
            Console.WriteLine(llmResponse);
            if (llmResponse.NeedResponse)
            {
                await adapter.SendMessageAsync(msgEvt.Target, [new Text(llmResponse.ChatText)]);
            }
            if (llmResponse.EndSession)
            {
                // write thread to file
                var json = thread.Serialize(JsonSerializerOptions.Web);
                File.WriteAllText($"llm_thread_{msgEvt.Target.Id}_{DateTimeOffset.Now.ToUnixTimeSeconds()}.json", json.ToString());
                _agentThreads.Remove(msgEvt.Target.Id);
            }
        }
    }

    [Description("Get current Utc DateTime.")]
    private static DateTime GetCurrentDateTime()
    {
        Console.WriteLine("GetCurrentDateTime called.");
        return DateTime.UtcNow;
    }
}