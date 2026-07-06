using Microsoft.Extensions.Logging;
using Milky.Net.Client;
using Milky.Net.Model;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Logging;
using WoofBot.Sdk.Models;
using Event = WoofBot.Sdk.Models.Event;

namespace WoofBot.Adapters.Milky;

public record MilkyConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Token { get; set; } = "";
    public LoggingConfig Logging { get; set; } = new();
}

public class MilkyAdapter(MilkyConfig config, ILogger<MilkyAdapter>? logger = null) : IAdapter
{
    public string Name => "Milky";
    public string SelfId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;

    public event Func<Event, IAdapter, Task>? OnEventReceived;

    private readonly MilkyConfig _config = config;
    private readonly ILogger<MilkyAdapter> _logger = logger ?? BotLog.CreateLogger<MilkyAdapter>();
    private MilkyClient? _client;
    private readonly CancellationTokenSource _cts = new();

    public async Task StartAsync()
    {
        if (_client is not null)
            throw new InvalidOperationException("Adapter is already started.");
        _logger.LogInformation(
            "Starting Milky adapter for {Host}:{Port}.",
            _config.Host,
            _config.Port
        );
        HttpClient httpClient = new()
        {
            BaseAddress = new Uri($"ws://{_config.Host}:{_config.Port}"),
            DefaultRequestHeaders = { Authorization = new("Bearer", _config.Token) },
        };
        _client = new MilkyClient(httpClient);
        var response = await _client.System.GetLoginInfoAsync();
        SelfId = response.Uin.ToString();
        Nickname = response.Nickname;
        _logger.LogInformation(
            "Milky adapter logged in as {Nickname} ({SelfId}).",
            Nickname,
            SelfId
        );
        _client.Events.MessageReceive += async (milky, evt) =>
        {
            Event? woofEvent = evt.ToWoofBotEvent();
            if (woofEvent is not null && OnEventReceived is not null)
            {
                await OnEventReceived.Invoke(woofEvent, this);
            }
        };
        _ = _client.ReceivingEventUsingWebSocketAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_client is not null)
        {
            _cts.Cancel();
            _client = null;
            _logger.LogInformation("Milky adapter stopped.");
        }
    }

    public async Task<long> SendMessageAsync(Target target, Messages messages)
    {
        if (_client is null)
            throw new InvalidOperationException("Adapter is not started.");
        if (messages is [UploadFile file])
        {
            string folderId = "/";
            if (file.Folder is not null && file.Folder != "/")
            {
                var list = await _client.File.GetGroupFilesAsync(new(long.Parse(target.Id)));
                if (!list.Folders.Any(folder => folder.FolderName == file.Folder))
                {
                    _logger.LogInformation(
                        "Creating folder {Folder} in group {GroupId}.",
                        file.Folder,
                        target.Id
                    );
                    var response = await _client.File.CreateGroupFolderAsync(
                        new(long.Parse(target.Id), file.Folder)
                    );
                    folderId = response.FolderId;
                }
                else
                {
                    folderId = list
                        .Folders.First(folder => folder.FolderName == file.Folder)
                        .FolderId;
                }
            }
            var result = await _client.File.GetGroupFilesAsync(
                new(long.Parse(target.Id), folderId)
            );
            if (!result.Files.Any(f => f.FileName == file.Name))
            {
                _logger.LogInformation(
                    "Uploading file {FileName} to group {GroupId} folder {Folder}.",
                    file.Name,
                    target.Id,
                    folderId
                );
                var response = await _client.File.UploadGroupFileAsync(
                    new(long.Parse(target.Id), new(file.Uri), file.Name, folderId)
                );
                return 0;
            }
            else
            {
                _logger.LogInformation(
                    "File {FileName} already exists in group {GroupId} folder {Folder}.",
                    file.Name,
                    target.Id,
                    folderId
                );
                var response = await _client.Message.SendGroupMessageAsync(
                    new(
                        long.Parse(target.Id),
                        [
                            new OutgoingSegment<TextOutgoingSegmentData>(
                                new(
                                    $"文件「{file.Name}」已存在于文件夹「{file.Folder}」中，不再重复上传~"
                                )
                            ),
                        ]
                    )
                );
                return response.MessageSeq;
            }
        }
        OutgoingSegment[] segments = messages.ToMilkySegments();
        switch (target.Type)
        {
            case TargetType.Private:
                var privateMsgResponse = await _client.Message.SendPrivateMessageAsync(
                    new(long.Parse(target.Id), segments)
                );
                return privateMsgResponse.MessageSeq;
            case TargetType.Group:
                var groupMsgResponse = await _client.Message.SendGroupMessageAsync(
                    new(long.Parse(target.Id), segments)
                );
                return groupMsgResponse.MessageSeq;
            default:
                throw new NotSupportedException($"Unsupported target type: {target.Type}");
        }
    }
}
