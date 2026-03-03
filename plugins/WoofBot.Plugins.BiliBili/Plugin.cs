using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    public List<string> MonitorGroups { get; init; } = [];
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
        var response = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/') + $"/video/latest?user_id={userId}"
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
        var response = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/') + $"/video/latest/image?user_id={userId}"
        );
        if (!response.IsSuccessStatusCode)
            return null;
        var imageBytes = await response.Content.ReadAsByteArrayAsync();
        string base64String = Convert.ToBase64String(imageBytes);
        return $"base64://{base64String}";
    }

    async Task<UserIdInfo?> GetUserIdByName(string username)
    {
        var response = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/') + $"/user/id?username={Uri.EscapeDataString(username)}"
        );
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<UserIdInfo>(
            json,
            new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        return result;
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
                if (!string.IsNullOrEmpty(videoInfo.Description))
                {
                    sb.AppendLine(videoInfo.Description);
                    sb.AppendLine();
                }
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

    private async Task<string> SubscribeUp(string groupId, string username)
    {
        UserIdInfo? userInfo = await GetUserIdByName(username);
        if (userInfo is null)
            return "找不到该up哦~";
        if (userInfo.Fans < 10000)
            return $"「{userInfo.Username}」的粉丝不足1万哦~是不是输错了？";
        var entry = Config.Subscriptions.FirstOrDefault(e => e.GroupId == groupId);
        if (entry is null)
        {
            entry = new SubscribeEntry { GroupId = groupId };
            Config.Subscriptions.Add(entry);
        }
        if (!entry.UserIds.Contains(userInfo.UserId))
        {
            entry.UserIds.Add(userInfo.UserId);
            if (!Config.LastPubTimes.ContainsKey(userInfo.UserId))
            {
                Config.LastPubTimes[userInfo.UserId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
            UpdateConfig();
            if (userInfo.Username != username)
            {
                return $"检测到相似up主「{userInfo.Username}」({userInfo.Fans}粉丝)，已自动切换为订阅该up的视频~";
            }
            return $"已订阅up「{userInfo.Username}」的视频~";
        }
        else
        {
            if (userInfo.Username != username)
            {
                return $"检测到相似up主「{userInfo.Username}」({userInfo.Fans}粉丝)，该up已经订阅过了哦~";
            }
            return $"up「{userInfo.Username}」已经订阅过了哦~";
        }
    }

    private async Task<string> UnsubscribeUp(string groupId, string username)
    {
        UserIdInfo? userInfo = await GetUserIdByName(username);
        if (userInfo is null)
            return "找不到该up哦~";
        var entry = Config.Subscriptions.FirstOrDefault(e => e.GroupId == groupId);
        if (entry is not null && entry.UserIds.Contains(userInfo.UserId))
        {
            entry.UserIds.Remove(userInfo.UserId);
            UpdateConfig();
            if (userInfo.Username != username)
            {
                return $"检测到相似up主「{userInfo.Username}」({userInfo.Fans}粉丝)，已自动切换为取消订阅该up的视频~";
            }
            return $"已取消订阅up「{userInfo.Username}」的视频~";
        }
        else
        {
            if (userInfo.Username != username)
            {
                return $"检测到相似up主「{userInfo.Username}」，但该up并没有订阅过哦~";
            }
            return $"up「{userInfo.Username}」并没有订阅过哦~";
        }
    }

    private async Task<List<Messages>> ParseBilibiliLink(string url, bool isLightApp)
    {
        url = url.Split('?')[0];
        string json;
        if (url.Contains("b23.tv"))
        {
            var response = await _httpClient.GetAsync(
                Config.RequestUrl.TrimEnd('/')
                    + $"/video/info/short_url?short_url={Uri.EscapeDataString(url)}"
            );
            if (!response.IsSuccessStatusCode)
                return
                [
                    [new Text("链接解析失败 ;-;")],
                ];
            json = await response.Content.ReadAsStringAsync();
        }
        else
        {
            string? bvid = url.Split('/').LastOrDefault(e => e.StartsWith("BV"));
            if (bvid is null)
                return
                [
                    [new Text("链接解析失败 ;-;")],
                ];
            json = await _httpClient.GetStringAsync(
                Config.RequestUrl.TrimEnd('/') + $"/video/info?bvid={Uri.EscapeDataString(bvid)}"
            );
        }
        var videoInfo = JsonSerializer.Deserialize<VideoInfo>(
            json,
            new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        if (videoInfo is null)
            return
            [
                [new Text("视频解析失败 ;-;")],
            ];
        List<Messages> message = [];
        var imageResponse = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/')
                + $"/video/info/image?bvid={Uri.EscapeDataString(videoInfo.Bvid)}"
        );
        if (imageResponse.IsSuccessStatusCode)
        {
            var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();
            string base64String = Convert.ToBase64String(imageBytes);
            message.Add([new Image($"base64://{base64String}")]);
        }
        if (!string.IsNullOrEmpty(videoInfo.Description))
        {
            message.Add([new Text(videoInfo.Description)]);
        }
        if (isLightApp)
        {
            message.Add([new Text($"链接：https://www.bilibili.com/video/{videoInfo.Bvid}")]);
        }
        return message;
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
                string username = text.Content["订阅up".Length..].Trim();
                string result = await SubscribeUp(msgEvt.Target.Id, username);
                await adapter.SendMessageAsync(
                    new Target(TargetType.Group, msgEvt.Target.Id),
                    [new Text(result)]
                );
            }
            else if (text.Content.StartsWith("取消订阅up"))
            {
                string username = text.Content["取消订阅up".Length..].Trim();
                string result = await UnsubscribeUp(msgEvt.Target.Id, username);
                await adapter.SendMessageAsync(
                    new Target(TargetType.Group, msgEvt.Target.Id),
                    [new Text(result)]
                );
            }
            else if (text.Content == "检查订阅更新")
            {
                var groups = new[] { msgEvt.Target.Id };
                await DoCheck(groups, adapter);
            }
            else if (text.Content == "启用b站链接解析")
            {
                if (!Config.MonitorGroups.Contains(msgEvt.Target.Id))
                {
                    Config.MonitorGroups.Add(msgEvt.Target.Id);
                    UpdateConfig();
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text("已启用b站链接解析~")]
                    );
                }
                else
                {
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text("已经启用了哦~")]
                    );
                }
            }
            else if (text.Content == "禁用b站链接解析")
            {
                if (Config.MonitorGroups.Contains(msgEvt.Target.Id))
                {
                    Config.MonitorGroups.Remove(msgEvt.Target.Id);
                    UpdateConfig();
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text("已禁用b站链接解析~")]
                    );
                }
                else
                {
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text("已经禁用了哦~")]
                    );
                }
            }
        }
        else if (evt is CronEvent cron && cron.TaskName == "bilibili-poll")
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
        if (
            evt is MessageEvent msgEvt2
            && msgEvt2.Target.Type == TargetType.Group
            && Config.MonitorGroups.Contains(msgEvt2.Target.Id)
        )
        {
            string? url = null;
            if (
                msgEvt2.Messages is [LightApp lightApp]
                && (lightApp.Title == "哔哩哔哩" || lightApp.Title == "哔哩哔哩HD")
            )
            {
                url = lightApp.Url;
            }
            else
            {
                foreach (var plainText in msgEvt2.Messages.OfType<Text>())
                {
                    if (
                        plainText.Content.Contains("b23.tv/")
                        || plainText.Content.Contains("bilibili.com/video/")
                    )
                    {
                        url = Regex
                            .Match(
                                plainText.Content,
                                @"(https?://)?(www\.)?(b23\.tv/[a-zA-Z0-9]+|bilibili\.com/video/[a-zA-Z0-9]+)"
                            )
                            .Value;
                        break;
                    }
                }
            }
            if (url is not null)
            {
                List<Messages> messages = await ParseBilibiliLink(
                    url,
                    msgEvt2.Messages is [LightApp]
                );
                foreach (var msgs in messages)
                {
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt2.Target.Id),
                        msgs
                    );
                    await Task.Delay(1000);
                }
            }
        }
    }

    public override void Subscribe(IAdapter adapter)
    {
        base.Subscribe(adapter);
        RegisterSchedule(
            "bilibili-poll",
            TimeSpan.FromMinutes(Config.PollIntervalMinutes),
            adapter,
            TimeSpan.FromSeconds(30)
        );
    }
}
