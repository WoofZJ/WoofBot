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
using OpenAI.Responses;
using System.Net.Http.Json;

namespace WoofBot.Plugins.LLM;

public record LLMPluginConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
    public List<string> EnabledGroups { get; init; } = [];
    public string InstructionsFilePath { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public List<string> WakeWords { get; init; } = [];
    public int ContextLength { get; init; }
    public string ImageModel { get; init; } = string.Empty;
    public string SpeechModel { get; init; } = string.Empty;
}

[Description("Structured response format that you should return.")]
public record LLMResponse
{
    [Description("Whether a response message is needed.")]
    public bool NeedResponse { get; init; } = false;
    [Description("The text messages to send back. You can break long messages into multiple parts, and each part will be sent sequentially. Don't break into too many parts to avoid spamming.")]
    public List<string> TextMessage { get; init; } = [];
    [Description("The image message URL to send back.")]
    public string ImageMessageUrl { get; init; } = string.Empty;
    [Description("Whether to end the whole session and clear the context.")]
    public bool EndWholeSession { get; init; } = false;
}

public class LLMPlugin : PluginBase<LLMPluginConfig>
{
    public LLMPlugin() : base("LLM", "1.0", "A simple LLM plugin") {}

    private ChatClientAgent? _agent;
    private Dictionary<string, AgentThread> _agentThreads = [];
    private HashSet<string> _activeSessions = [];

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
                        Tools = [
                            AIFunctionFactory.Create(GetCurrentDateTime),
                            AIFunctionFactory.Create(GenerateImage),
                            AIFunctionFactory.Create(EditImage),
                        ],
                        RawRepresentationFactory = _ => new ChatCompletionOptions
                        {
                            #pragma warning disable OPENAI001
                            ReasoningEffortLevel = "low",
                            #pragma warning restore OPENAI001
                        },
                    },
                    ChatMessageStoreFactory = ctx => new InMemoryChatMessageStore(
                        #pragma warning disable MEAI001
                        new MessageCountingChatReducer(Config.ContextLength),
                        #pragma warning restore MEAI001
                        ctx.SerializedState, ctx.JsonSerializerOptions)
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
        AppendChatMessage(adapter, msgEvt);
        if (!_activeSessions.Contains(msgEvt.Target.Id) &&
            (msgEvt.Messages.Contains(new At(adapter.SelfId)) ||
                msgEvt.Messages.Any(m => m is Text text && Config.WakeWords.Any(ww => text.Content.Contains(ww)))))
        {

            _activeSessions.Add(msgEvt.Target.Id);
            Console.WriteLine($"[LLMPlugin] Activated session for group {msgEvt.Target.Id}");
        }
        if (_activeSessions.Contains(msgEvt.Target.Id) && 
            _agentThreads.TryGetValue(msgEvt.Target.Id, out AgentThread? thread))
        {
            var response = await _agent.RunAsync<LLMResponse>(thread);
            var llmResponse = response.Deserialize<LLMResponse>(JsonSerializerOptions.Web);
            Console.WriteLine(llmResponse);
            if (llmResponse.NeedResponse)
            {
                foreach (var textMsg in llmResponse.TextMessage)
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text(textMsg)]
                    );
                    await Task.Delay(Random.Shared.Next(1000, 3000));
                }
                if (!string.IsNullOrEmpty(llmResponse.ImageMessageUrl))
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Image(llmResponse.ImageMessageUrl)]
                    );
                }
            }
            if (llmResponse.EndWholeSession)
            {
                var json = thread.Serialize(JsonSerializerOptions.Web);
                File.WriteAllText($"llm_thread_{msgEvt.Target.Id}_{DateTimeOffset.Now.ToUnixTimeSeconds()}.json", json.ToString());
                _agentThreads.Remove(msgEvt.Target.Id);
                _activeSessions.Remove(msgEvt.Target.Id);
            }
        }
    }

    private void AppendChatMessage(IAdapter adapter, MessageEvent msgEvt)
    {
        if (_agent is null) return;
        if (!_agentThreads.TryGetValue(msgEvt.Target.Id, out AgentThread? thread))
        {
            thread = _agent.GetNewThread();
            _agentThreads[msgEvt.Target.Id] = thread;
        }
        StringBuilder sb = new($"User({msgEvt.SenderId}): ");
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
                    if (img.FileSize <= 4 * 1024 * 1024)
                    {
                        images.Add(img.Url);
                        sb.Append($"[图片,url={img.Url}]");
                    }
                    else
                    {
                        sb.Append("[图片,文件过大未接收]");
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
        var history = thread.GetService<IList<ChatMessage>>();
        Console.WriteLine("history has count: " + history?.Count);
        history?.Add(userMessage);
        Console.WriteLine($"Appended user message {userMessage} to thread.");
    }

    [Description("Get current Utc DateTime.")]
    private static DateTime GetCurrentDateTime()
    {
        Console.WriteLine("GetCurrentDateTime called.");
        return DateTime.UtcNow;
    }

    [Description("Generate an image based on the given prompt. Returns the image URL.")]
    private async Task<string> GenerateImage(
        [Description("The prompt describing the image to generate. You can describe the style, content, colors, and other details of the image you want.")]
        string prompt)
    {
        try
        {
            Console.WriteLine($"Generating image with prompt: {prompt}");
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.ApiKey);

            var requestBody = new
            {
                model = Config.ImageModel,
                prompt = prompt,
                sequential_image_generation = "disabled",
                response_format = "url",
                size = "2K",
                stream = false,
                watermark = true
            };
            var response = await httpClient.PostAsJsonAsync($"{Config.Endpoint}/images/generations", requestBody);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (jsonResponse.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var url = data[0].GetProperty("url").GetString();
                Console.WriteLine($"Generated image URL: {url}");
                return url ?? "Error: Image URL not found.";
            }
            return "Error: Failed to parse image URL from response.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating image: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Edit an existing image based on the given prompt. Returns the edited image URL.")]
    private async Task<string> EditImage(
        [Description("The URL of the image to be edited.")]
        string imageUrl,
        [Description("The prompt describing the edits to be made to the image.")]
        string prompt)
    {
        try
        {
            Console.WriteLine($"Editing image {imageUrl} with prompt: {prompt}");
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.ApiKey);

            var requestBody = new
            {
                model = Config.ImageModel,
                prompt = prompt,
                image = imageUrl,
                sequential_image_generation = "disabled",
                response_format = "url",
                size = "2K",
                stream = false,
                watermark = true
            };

            var response = await httpClient.PostAsJsonAsync($"{Config.Endpoint}/images/generations", requestBody);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (jsonResponse.TryGetProperty("data", out var data) && data.GetArrayLength() > 0)
            {
                var url = data[0].GetProperty("url").GetString();
                Console.WriteLine($"Edited image URL: {url}");
                return url ?? "Error: Image URL not found.";
            }

            return "Error: Failed to parse image URL from response.";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error editing image: {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }
}