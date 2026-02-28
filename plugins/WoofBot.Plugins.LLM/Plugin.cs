using System.ClientModel;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;
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
    public int ContextLength { get; init; }
    public string ImageModel { get; init; } = string.Empty;
    public string ImageEndpoint { get; init; } = string.Empty;
    public string ImageApiKey { get; init; } = string.Empty;
    public string VideoModel { get; init; } = string.Empty;
    public int DebounceDelayMs { get; init; }
}

[Description("你应该遵循的响应格式")]
public record LLMResponse
{
    [Description(
        "是否需要发送回复消息。你处在群聊中，不是所有消息都需要回复，你应该只对明确提及到你的消息进行回复，只有在需要回复的时候才将此字段设为 true。"
    )]
    public bool NeedResponse { get; init; } = false;

    [Description(
        "要发送回去的文本消息。你可以将长消息拆分成多部分，每部分会依次发送。不要拆分成太多部分以避免刷屏。"
    )]
    public List<string> TextMessage { get; init; } = [];

    [Description("要发送回去的图片消息 URL。")]
    public string ImageMessageUrl { get; init; } = string.Empty;

    [Description("要发送回去的视频消息任务 ID。")]
    public string VideoTaskId { get; init; } = string.Empty;
}

public class LLMPlugin : PluginBase<LLMPluginConfig>
{
    public LLMPlugin()
        : base("LLM", "1.0", "A simple LLM plugin") { }

    private ChatClientAgent? _agent;
    private Dictionary<string, AgentThread> _agentThreads = [];
    private HashSet<string> _activeSessions = [];
    private readonly ConcurrentDictionary<string, DateTime> _sessionLastActiveTime = new(); // Session timeout tracking

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounceCts = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ChatMessage>> _pendingMessages =
        new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _groupLocks = new();
    private Timer _videoQueryTimer;

    public override void Initialize(string configDir)
    {
        base.Initialize(configDir);
        if (
            string.IsNullOrEmpty(Config.ApiKey)
            || string.IsNullOrEmpty(Config.Endpoint)
            || string.IsNullOrEmpty(Config.InstructionsFilePath)
            || string.IsNullOrEmpty(Config.ModelName)
        )
        {
            Console.WriteLine(
                "LLM Plugin is not properly configured. API key, Endpoint, InstructionsFilePath, or ModelName is missing."
            );
            return;
        }
        var instructions = File.ReadAllText(Config.InstructionsFilePath);
        OpenAIClient client = new(
            new ApiKeyCredential(Config.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(Config.Endpoint) }
        );
        _agent = client
            .GetChatClient(Config.ModelName)
            .CreateAIAgent(
                new ChatClientAgentOptions()
                {
                    Name = "WoofBot LLM Agent",
                    ChatOptions = new()
                    {
                        Instructions = instructions,
                        ResponseFormat =
                            Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema<LLMResponse>(),
                        Tools =
                        [
                            AIFunctionFactory.Create(GetCurrentDateTime),
                            AIFunctionFactory.Create(GenerateImage),
                            AIFunctionFactory.Create(EditImage),
                            AIFunctionFactory.Create(GenerateVideo),
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
                        ctx.SerializedState,
                        ctx.JsonSerializerOptions
                    ),
                }
            );
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (evt is not MessageEvent || _agent is null)
            return;
        var msgEvt = (MessageEvent)evt;
        if (
            msgEvt.Target.Type is not TargetType.Group
            || !Config.EnabledGroups.Contains(msgEvt.Target.Id)
        )
            return;

        var chatMessage = CreateChatMessage(adapter, msgEvt);
        var queue = _pendingMessages.GetOrAdd(
            msgEvt.Target.Id,
            _ => new ConcurrentQueue<ChatMessage>()
        );
        queue.Enqueue(chatMessage);

        lock (_activeSessions)
        {
            bool isActive = _activeSessions.Contains(msgEvt.Target.Id);

            // Check expiry
            if (isActive && _sessionLastActiveTime.TryGetValue(msgEvt.Target.Id, out var lastTime))
            {
                if ((DateTime.UtcNow - lastTime).TotalMinutes > 5)
                {
                    _activeSessions.Remove(msgEvt.Target.Id);
                    isActive = false;
                    Console.WriteLine($"[LLMPlugin] Session expired for group {msgEvt.Target.Id}");
                }
            }

            // Check activation
            bool isWakeWord =
                msgEvt.Messages.Contains(new At(adapter.SelfId))
                || msgEvt.Messages.Any(m =>
                    m is Text text && Config.WakeWords.Any(ww => text.Content.Contains(ww))
                );

            if (isWakeWord)
            {
                if (!isActive)
                {
                    _activeSessions.Add(msgEvt.Target.Id);
                    isActive = true;
                    Console.WriteLine(
                        $"[LLMPlugin] Activated session for group {msgEvt.Target.Id}"
                    );
                }
            }

            // Update timestamp
            if (isActive)
            {
                _sessionLastActiveTime[msgEvt.Target.Id] = DateTime.UtcNow;
            }
        }

        if (_debounceCts.TryGetValue(msgEvt.Target.Id, out var oldCts))
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }

        var newCts = new CancellationTokenSource();
        _debounceCts[msgEvt.Target.Id] = newCts;

        _ = ProcessDebounceAsync(msgEvt.Target, adapter, newCts.Token);

        await Task.CompletedTask;
    }

    private async Task ProcessDebounceAsync(
        Target target,
        IAdapter adapter,
        CancellationToken token
    )
    {
        try
        {
            await Task.Delay(Config.DebounceDelayMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        var semaphore = _groupLocks.GetOrAdd(target.Id, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();

        try
        {
            if (token.IsCancellationRequested)
                return;

            if (!_agentThreads.TryGetValue(target.Id, out AgentThread? thread))
            {
                thread = _agent!.GetNewThread();
                _agentThreads[target.Id] = thread;
            }

            var history = thread.GetService<IList<ChatMessage>>();
            if (_pendingMessages.TryGetValue(target.Id, out var queue))
            {
                while (queue.TryDequeue(out var msg))
                {
                    history?.Add(msg);
                    Console.WriteLine(
                        $"[Debounce] Flushed message to thread history. Queue count: {queue.Count}"
                    );
                }
            }

            bool isActiveSession = false;
            lock (_activeSessions)
            {
                isActiveSession = _activeSessions.Contains(target.Id);
            }

            if (isActiveSession)
            {
                Console.WriteLine($"[Debounce] Invoking Agent for group {target.Id}");
                var response = await _agent!.RunAsync<LLMResponse>(thread);
                var llmResponse = response.Deserialize<LLMResponse>(JsonSerializerOptions.Web);
                Console.WriteLine(llmResponse);

                if (llmResponse.NeedResponse)
                {
                    foreach (var textMsg in llmResponse.TextMessage)
                    {
                        await adapter.SendMessageAsync(target, [new Text(textMsg)]);
                        await Task.Delay(Random.Shared.Next(1500, 4000));
                    }
                    if (!string.IsNullOrEmpty(llmResponse.ImageMessageUrl))
                    {
                        await adapter.SendMessageAsync(
                            target,
                            [new Image(llmResponse.ImageMessageUrl)]
                        );
                    }
                    if (!string.IsNullOrEmpty(llmResponse.VideoTaskId))
                    {
                        _videoQueryTimer = new Timer(
                            async _ =>
                            {
                                try
                                {
                                    using var httpClient = new HttpClient();
                                    httpClient.DefaultRequestHeaders.Authorization =
                                        new System.Net.Http.Headers.AuthenticationHeaderValue(
                                            "Bearer",
                                            Config.ImageApiKey
                                        );
                                    var statusResponse = await httpClient.GetAsync(
                                        $"{Config.ImageEndpoint}/contents/generations/tasks/{llmResponse.VideoTaskId}"
                                    );
                                    statusResponse.EnsureSuccessStatusCode();
                                    var statusJson =
                                        await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
                                    Console.WriteLine(
                                        $"[VideoQuery] Video generation status response: {statusJson}"
                                    );
                                    if (
                                        statusJson.TryGetProperty("status", out var statusProp)
                                        && statusProp.GetString() == "succeeded"
                                    )
                                    {
                                        if (
                                            statusJson.TryGetProperty(
                                                "content",
                                                out var contentProp
                                            )
                                            && contentProp.TryGetProperty(
                                                "video_url",
                                                out var videoUrlProp
                                            )
                                        )
                                        {
                                            var videoUrl = videoUrlProp.GetString();
                                            Console.WriteLine(
                                                $"[VideoQuery] Generated video URL: {videoUrl}"
                                            );
                                            await adapter.SendMessageAsync(
                                                target,
                                                [new Text("视频生成好啦！")]
                                            );
                                            await adapter.SendMessageAsync(
                                                target,
                                                [new Video(videoUrl ?? "")]
                                            );
                                        }
                                        else
                                        {
                                            Console.WriteLine(
                                                $"[VideoQuery] Video URL not found in content."
                                            );
                                        }
                                        _videoQueryTimer?.Dispose();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(
                                        $"[VideoQuery] Error querying video generation status: {ex}"
                                    );
                                }
                            },
                            null,
                            1000,
                            20000
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Debounce] Error processing group {target.Id}: {ex}");
        }
        finally
        {
            semaphore.Release();
        }
    }

    private ChatMessage CreateChatMessage(IAdapter adapter, MessageEvent msgEvt)
    {
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
                    sb.Append(
                        at.Target == adapter.SelfId
                            ? $"@({Config.WakeWords.First()})"
                            : $"@({at.Target})"
                    );
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
                default:
                    sb.Append("[未知消息类型]");
                    break;
            }
        }
        List<AIContent> contents = [new TextContent(sb.ToString())];
        foreach (var imgUrl in images)
        {
            contents.Add(new UriContent(imgUrl, "image/jpeg"));
        }
        return new ChatMessage(ChatRole.User, contents);
    }

    [Description("Get current Utc DateTime.")]
    private static DateTime GetCurrentDateTime()
    {
        Console.WriteLine("GetCurrentDateTime called.");
        return DateTime.UtcNow;
    }

    [Description("Generate an image based on the given prompt. Returns the image URL.")]
    private async Task<string> GenerateImage(
        [Description(
            "The prompt describing the image to generate. You can describe the style, content, colors, and other details of the image you want."
        )]
            string prompt
    )
    {
        try
        {
            Console.WriteLine($"Generating image with prompt: {prompt}");
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.ImageApiKey);

            var requestBody = new
            {
                model = Config.ImageModel,
                prompt = prompt,
                sequential_image_generation = "disabled",
                response_format = "url",
                size = "2K",
                stream = false,
                watermark = true,
            };
            var response = await httpClient.PostAsJsonAsync(
                $"{Config.ImageEndpoint}/images/generations",
                requestBody
            );
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
        [Description("The URL of the image to be edited.")] string imageUrl,
        [Description("The prompt describing the edits to be made to the image.")] string prompt
    )
    {
        try
        {
            Console.WriteLine($"Editing image {imageUrl} with prompt: {prompt}");
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.ImageApiKey);

            var requestBody = new
            {
                model = Config.ImageModel,
                prompt = prompt,
                image = imageUrl,
                sequential_image_generation = "disabled",
                response_format = "url",
                size = "2K",
                stream = false,
                watermark = true,
            };

            var response = await httpClient.PostAsJsonAsync(
                $"{Config.ImageEndpoint}/images/generations",
                requestBody
            );
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

    [Description(
        "Generate a video based on the given prompt. Returns the video task id. Tell the user the video is being generated and they can wait for a while."
    )]
    private async Task<string> GenerateVideo(
        [Description("The prompt describing the video to generate.")] string prompt,
        [Description(
            "The URL of the image to be used as a reference for video generation. Set it as empty string if not used."
        )]
            string ImageUrl
    )
    {
        try
        {
            Console.WriteLine($"Generating video with prompt: {prompt}");
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Config.ImageApiKey);

            List<object> contentList = new() { new { @type = "text", text = prompt } };
            if (!string.IsNullOrEmpty(ImageUrl))
            {
                contentList.Add(new { @type = "image_url", image_url = new { url = ImageUrl } });
            }
            var requestBody = new { model = Config.VideoModel, content = contentList };

            var response = await httpClient.PostAsJsonAsync(
                $"{Config.ImageEndpoint}/contents/generations/tasks",
                requestBody
            );
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadFromJsonAsync<JsonElement>();
            Console.WriteLine($"Video generation response: {jsonResponse}");
            if (jsonResponse.TryGetProperty("id", out var idJson))
            {
                string taskId = idJson.GetString() ?? string.Empty;
                return taskId;
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
