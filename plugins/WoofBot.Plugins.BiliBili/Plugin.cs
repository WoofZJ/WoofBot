using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;

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

    async Task<VideoInfo?> GetVideoInfoAsync(long userId, int retryCount = 0)
    {
        var response = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/') + $"/bilibili/video/latest?user_id={userId}"
        );
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            if (retryCount >= 3)
            {
                Console.WriteLine(
                    $"[{DateTimeOffset.Now:T}] 重试3次，放弃获取用户 {userId} 的视频信息"
                );
                return null;
            }
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            await Task.Delay(retryAfter);
            return await GetVideoInfoAsync(userId);
        }
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync();
        var videoInfo = JsonSerializer.Deserialize<VideoInfo>(
            json,
            new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        return videoInfo;
    }

    async Task<string?> GetImageAsync(string route)
    {
        var response = await _httpClient.GetAsync(Config.RequestUrl.TrimEnd('/') + route);
        if (!response.IsSuccessStatusCode)
            return null;
        var imageBytes = await response.Content.ReadAsByteArrayAsync();
        string base64 = Convert.ToBase64String(imageBytes);
        return $"base64://{base64}";
    }

    async Task<UserIdInfo?> GetUserIdByName(string username)
    {
        var response = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/')
                + $"/bilibili/user/id?username={Uri.EscapeDataString(username)}"
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

    async Task<Dictionary<long, (string bvid, List<Messages> messages)>> UpdateSubscribe(
        HashSet<long> userIds
    )
    {
        Dictionary<long, (string bvid, List<Messages> messages)> result = [];
        Console.WriteLine(
            $"[{DateTimeOffset.Now:T}] Checking updates for user IDs: {string.Join(", ", userIds)}"
        );
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
                StringBuilder sb = new();
                string AuthorName =
                    videoInfo.Staffs.FirstOrDefault(s => s.Mid == userId)?.Name
                    ?? videoInfo.AuthorName;
                sb.Append(
                    $"「{AuthorName}」于{TimeSpanToString(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(videoInfo.PublishTime))}前更新了！"
                );
                if (videoInfo.Staffs.Length > 0)
                {
                    sb.Append("是与");
                    sb.AppendJoin(
                        "、",
                        videoInfo.Staffs.Where(s => s.Mid != userId).Select(s => $"「{s.Name}」")
                    );
                    sb.Append("的联合投稿！");
                }
                messages.Add([new Text(sb.ToString().Trim())]);
                string? image = await GetImageAsync(
                    "/bilibili/video/info/image?bvid=" + Uri.EscapeDataString(videoInfo.Bvid)
                );
                if (image is not null)
                {
                    messages.Add([new Image(image)]);
                }
                sb.Clear();
                string desc = ProcessDescription(videoInfo.Description);
                if (!string.IsNullOrEmpty(desc))
                {
                    sb.AppendLine(desc);
                    sb.AppendLine();
                }
                sb.AppendLine($"链接：https://www.bilibili.com/video/{videoInfo.Bvid}");
                messages.Add([new Text(sb.ToString().Trim())]);
                Config.LastPubTimes[userId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                UpdateConfig();
            }
            result[userId] = (videoInfo?.Bvid ?? string.Empty, messages);
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
            HashSet<string> bvids = [];
            foreach (var userId in entry.UserIds)
            {
                if (updates.ContainsKey(userId))
                {
                    var (bvid, messages) = updates[userId];
                    if (!string.IsNullOrEmpty(bvid) && !bvids.Contains(bvid))
                    {
                        bvids.Add(bvid);
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

    private static string ProcessDescription(string description)
    {
        if (!string.IsNullOrWhiteSpace(description) && description.Trim().Length < 120)
        {
            return description.Trim();
        }
        return string.Empty;
    }

    private async Task<List<Messages>> ParseBilibiliLink(string url, bool isLightApp)
    {
        url = url.Split('?')[0];
        string json;
        if (url.Contains("b23.tv"))
        {
            var response = await _httpClient.GetAsync(
                Config.RequestUrl.TrimEnd('/')
                    + $"/bilibili/video/info/short_url?short_url={Uri.EscapeDataString(url)}"
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
                Config.RequestUrl.TrimEnd('/')
                    + $"/bilibili/video/info?bvid={Uri.EscapeDataString(bvid)}"
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
        string? image = await GetImageAsync(
            $"/bilibili/video/info/image?bvid={Uri.EscapeDataString(videoInfo.Bvid)}"
        );
        if (image is not null)
        {
            message.Add([new Image(image)]);
        }
        image = await GetImageAsync(
            $"/bilibili/video/comments/image?bvid={Uri.EscapeDataString(videoInfo.Bvid)}"
        );
        if (image is not null)
        {
            message.Add([new Image(image)]);
        }
        StringBuilder sb = new();
        string desc = ProcessDescription(videoInfo.Description);
        if (!string.IsNullOrEmpty(desc))
        {
            sb.AppendLine(desc);
            sb.AppendLine();
        }
        if (isLightApp)
        {
            sb.AppendLine($"链接：https://www.bilibili.com/video/{videoInfo.Bvid}");
        }
        if (sb.Length > 0)
        {
            message.Add([new Text(sb.ToString().Trim())]);
        }
        return message;
    }

    private async Task<List<Messages>> ParseDouyinLink(string url)
    {
        var response = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/') + $"/douyin/work/info?url={Uri.EscapeDataString(url)}"
        );
        if (!response.IsSuccessStatusCode)
            return
            [
                [new Text("链接解析失败 ;-;")],
            ];
        var json = await response.Content.ReadAsStringAsync();
        var videoInfo = JsonSerializer.Deserialize<DouyinVideoInfo>(
            json,
            new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        if (videoInfo is null)
            return
            [
                [new Text("视频解析失败 ;-;")],
            ];
        List<Messages> message = [];
        if (videoInfo.VideoSize < 10 * 1024 * 1024) // 10MB
        {
            message.Add([new Video(videoInfo.VideoUrl)]);
        }
        string? image = await GetImageAsync(
            $"/douyin/work/info/image?url={Uri.EscapeDataString(url)}"
        );
        if (image is not null)
        {
            message.Add([new Image(image)]);
        }
        image = await GetImageAsync($"/douyin/work/comments/image?url={Uri.EscapeDataString(url)}");
        if (image is not null)
        {
            message.Add([new Image(image)]);
        }
        return message;
    }

    private async Task<List<Messages>> ParseYoutubeLink(string url)
    {
        var response = await _httpClient.GetAsync(
            Config.RequestUrl.TrimEnd('/')
                + $"/youtube/video/info/url?url={Uri.EscapeDataString(url)}"
        );
        if (!response.IsSuccessStatusCode)
            return
            [
                [new Text("链接解析失败 ;-;")],
            ];
        var json = await response.Content.ReadAsStringAsync();
        var videoInfo = JsonSerializer.Deserialize<YoutubeVideoInfo>(
            json,
            new JsonSerializerOptions() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        if (videoInfo is null)
            return
            [
                [new Text("视频解析失败 ;-;")],
            ];
        List<Messages> message = [];
        string? image = await GetImageAsync(
            $"/youtube/video/info/url/image?url={Uri.EscapeDataString(url)}"
        );
        if (image is not null)
        {
            message.Add([new Image(image)]);
        }
        image = await GetImageAsync(
            $"/youtube/video/comments/image?video_id={Uri.EscapeDataString(videoInfo.VideoId)}"
        );
        if (image is not null)
        {
            message.Add([new Image(image)]);
        }
        string desc = ProcessDescription(videoInfo.Description);
        if (!string.IsNullOrEmpty(desc))
        {
            message.Add([new Text(desc)]);
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
            else if (text.Content == "启用视频链接解析")
            {
                if (!Config.MonitorGroups.Contains(msgEvt.Target.Id))
                {
                    Config.MonitorGroups.Add(msgEvt.Target.Id);
                    UpdateConfig();
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text("已启用链接解析~")]
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
            else if (text.Content == "禁用视频链接解析")
            {
                if (Config.MonitorGroups.Contains(msgEvt.Target.Id))
                {
                    Config.MonitorGroups.Remove(msgEvt.Target.Id);
                    UpdateConfig();
                    await adapter.SendMessageAsync(
                        new Target(TargetType.Group, msgEvt.Target.Id),
                        [new Text("已禁用链接解析~")]
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
                    if (plainText.Content.Contains("douyin.com"))
                    {
                        url = Regex
                            .Match(
                                plainText.Content,
                                @"(https?://)?((v\.)|(www\.))?(douyin\.com/[a-zA-Z0-9!-~]+)"
                            )
                            .Value;
                        break;
                    }
                    if (plainText.Content.Contains("youtu.be/"))
                    {
                        url = Regex
                            .Match(
                                plainText.Content,
                                @"(https?://)?(www\.)?(youtu\.be/[a-zA-Z0-9_-]+)"
                            )
                            .Value;
                        break;
                    }
                    if (plainText.Content.Contains("youtube.com/"))
                    {
                        url = Regex
                            .Match(
                                plainText.Content,
                                @"(https?://)?(www\.)?(youtube\.com/watch\?v=[a-zA-Z0-9_-]+)"
                            )
                            .Value;
                        break;
                    }
                }
            }
            if (url is not null)
            {
                if (url.Contains("douyin.com"))
                {
                    List<Messages> messages = await ParseDouyinLink(url);
                    foreach (var msgs in messages)
                    {
                        await adapter.SendMessageAsync(
                            new Target(TargetType.Group, msgEvt2.Target.Id),
                            msgs
                        );
                        await Task.Delay(1000);
                    }
                }
                else if (url.Contains("b23.tv/") || url.Contains("bilibili.com/video/"))
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
                else if (url.Contains("youtu.be/") || url.Contains("youtube.com/"))
                {
                    if (Config.Admins.Contains(msgEvt2.SenderId))
                    {
                        List<Messages> messages = await ParseYoutubeLink(url);
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
        }
    }

    public override void Subscribe(IAdapter adapter)
    {
        base.Subscribe(adapter);
        RegisterSchedule(
            "bilibili-poll",
            "10,40 * * * *",
            async (_) =>
            {
                var groups = Config.Subscriptions.Select(e => e.GroupId).ToArray();
                await DoCheck(groups, adapter);
            }
        );
    }
}
