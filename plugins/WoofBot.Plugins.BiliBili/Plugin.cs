using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;
using WoofBot.Sdk.Serialization;

namespace WoofBot.Plugins.BiliBili;

public record SubscribeEntry
{
    public string GroupId { get; init; } = "";
    public List<long> UserIds { get; init; } = [];
}

public record BiliBiliPluginConfig
{
    public List<SubscribeEntry> Subscriptions { get; init; } = [];
    public List<string> Admins { get; init; } = [];
    public int PollIntervalMinutes { get; init; } = 10;
    public string RequestUrl { get; init; } = "";
    public Dictionary<long, long> LastPubTimes { get; init; } = [];
}

public class BiliBiliPlugin : PluginBase<BiliBiliPluginConfig>
{
    private static readonly HttpClient _httpClient = new();

    public BiliBiliPlugin()
        : base("BiliBili", "1.0", "A BiliBili plugin") { }

    private string TimeSpanToString(TimeSpan ts)
    {
        if (ts.Days > 0)
            return $"{ts.Days}天{ts.Hours}小时{ts.Minutes}分钟";
        else if (ts.Hours > 0)
            return $"{ts.Hours}小时{ts.Minutes}分钟";
        else if (ts.Minutes > 0)
            return $"{ts.Minutes}分钟";
        else
            return $"{ts.Seconds}秒";
    }

    async Task<VideoInfo?> GetVideoInfoAsync(long userId)
    {
        var response = await _httpClient.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                Config.RequestUrl.TrimEnd('/') + $"/video/latest?user_id={userId}"
            )
        );
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync();
        var videoInfo = JsonSerializer.Deserialize<VideoInfo>(
            json,
            new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        return videoInfo;
    }

    async Task<string?> GetVideoInfoImageAsync(long userId)
    {
        var response = await _httpClient.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                Config.RequestUrl.TrimEnd('/') + $"/video/latest/image?user_id={userId}"
            )
        );
        if (!response.IsSuccessStatusCode)
            return null;
        var imageBytes = await response.Content.ReadAsByteArrayAsync();
        string base64String = Convert.ToBase64String(imageBytes);
        return $"base64://{base64String}";
    }

    async Task<Dictionary<long, List<Messages>>> UpdateSubscribe(HashSet<long> userIds)
    {
        Dictionary<long, List<Messages>> result = [];
        Console.WriteLine($"Checking updates for user IDs: {string.Join(", ", userIds)}");
        foreach (var userId in userIds)
        {
            List<Messages> messages = [];
            if (!Config.LastPubTimes.ContainsKey(userId))
            {
                Config.LastPubTimes[userId] = 0;
            }
            long lastPubTime = Config.LastPubTimes[userId];
            var videoInfo = await GetVideoInfoAsync(userId);
            if (videoInfo is not null && videoInfo.PublishTime > lastPubTime)
            {
                Console.WriteLine(
                    $"User {userId} has a new video published at {DateTimeOffset.FromUnixTimeSeconds(videoInfo.PublishTime)} (last checked at {DateTimeOffset.FromUnixTimeSeconds(lastPubTime)})"
                );
                messages.Add([
                    new Text(
                        $"「{videoInfo.AuthorName}」于{TimeSpanToString(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(videoInfo.PublishTime))}前更新了！"
                    ),
                ]);
                string? image = await GetVideoInfoImageAsync(userId);
                if (image is not null)
                {
                    messages.Add([new Image(image)]);
                }
                StringBuilder sb = new();
                sb.AppendLine(videoInfo.Description);
                sb.AppendLine();
                sb.AppendLine($"链接：https://www.bilibili.com/video/{videoInfo.Bvid}");
                messages.Add([new Text(sb.ToString().TrimEnd())]);
                Config.LastPubTimes[userId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                UpdateConfig();
            }
            result[userId] = messages;
        }
        return result;
    }

    private async Task DoCheck(string[] groups, IAdapter adapter)
    {
        HashSet<long> userIds = [];
        foreach (var entry in Config.Subscriptions)
        {
            if (!groups.Contains(entry.GroupId))
                continue;
            foreach (var userId in entry.UserIds)
            {
                userIds.Add(userId);
            }
        }
        var updates = await UpdateSubscribe(userIds);
        foreach (var entry in Config.Subscriptions)
        {
            if (!groups.Contains(entry.GroupId))
                continue;
            foreach (var userId in entry.UserIds)
            {
                if (updates.ContainsKey(userId))
                {
                    List<Messages> messages = updates[userId];
                    foreach (var msg in messages)
                    {
                        await adapter.SendMessageAsync(
                            new Target(TargetType.Group, entry.GroupId),
                            msg
                        );
                        await Task.Delay(1000);
                    }
                }
            }
        }
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (
            evt is MessageEvent msgEvt
            && msgEvt.Target.Type == TargetType.Group
            && Config.Admins.Contains(msgEvt.SenderId)
            && msgEvt.Messages is [Text text]
        )
        {
            if (text.Content.StartsWith("订阅up"))
            {
                string userId = text.Content["订阅up".Length..].Trim();
                if (long.TryParse(userId, out long uid))
                {
                    var entry = Config.Subscriptions.FirstOrDefault(e =>
                        e.GroupId == msgEvt.Target.Id
                    );
                    if (entry is null)
                    {
                        entry = new SubscribeEntry { GroupId = msgEvt.Target.Id };
                        Config.Subscriptions.Add(entry);
                    }
                    if (!entry.UserIds.Contains(uid))
                    {
                        entry.UserIds.Add(uid);
                        UpdateConfig();
                        await adapter.SendMessageAsync(
                            new Target(TargetType.Group, msgEvt.Target.Id),
                            [new Text($"已订阅用户 {uid} 的动态~")]
                        );
                    }
                    else
                    {
                        await adapter.SendMessageAsync(
                            new Target(TargetType.Group, msgEvt.Target.Id),
                            [new Text($"用户 {uid} 已经订阅过了~")]
                        );
                    }
                }
                else
                {
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text($"用户ID应该是纯数字哦~")]
                    );
                }
            }
            else if (text.Content.StartsWith("取消订阅up"))
            {
                string userId = text.Content["取消订阅up".Length..].Trim();
                if (long.TryParse(userId, out long uid))
                {
                    var entry = Config.Subscriptions.FirstOrDefault(e =>
                        e.GroupId == msgEvt.Target.Id
                    );
                    if (entry is not null && entry.UserIds.Contains(uid))
                    {
                        entry.UserIds.Remove(uid);
                        UpdateConfig();
                        await adapter.SendMessageAsync(
                            new Target(TargetType.Group, msgEvt.Target.Id),
                            [new Text($"已取消订阅用户 {uid} 的动态~")]
                        );
                    }
                    else
                    {
                        await adapter.SendMessageAsync(
                            new Target(TargetType.Group, msgEvt.Target.Id),
                            [new Text($"用户 {uid} 没有订阅过哦~")]
                        );
                    }
                }
                else
                {
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text($"用户ID应该是纯数字哦~")]
                    );
                }
            }
            else if (text.Content == "检查订阅更新")
            {
                var groups = new[] { msgEvt.Target.Id };
                await DoCheck(groups, adapter);
            }
        }
        if (evt is CronEvent cron && cron.TaskName == "bilibili-poll")
        {
            if (DateTimeOffset.Now.Hour >= 8)
            {
                var groups = Config.Subscriptions.Select(e => e.GroupId).ToArray();
                await DoCheck(groups, adapter);
            }
            else
            {
                Console.WriteLine(
                    $"scheduled check triggered, but now is {DateTimeOffset.Now}. Skipped."
                );
            }
        }
    }

    public override void Subscribe(IAdapter adapter)
    {
        base.Subscribe(adapter);
        RegisterSchedule(
            "bilibili-poll",
            TimeSpan.FromMinutes(Config.PollIntervalMinutes),
            adapter
        );
    }
}
