using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    public string PythonPath { get; init; } = "python";
    public string ScriptPath { get; init; } = "";
    public string OutputPath { get; init; } = "";
    public string Proxy { get; init; } = "";
    public Dictionary<long, long> LastPubTimes { get; init; } = [];
}

public class BiliBiliPlugin : PluginBase<BiliBiliPluginConfig>
{
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

    Dictionary<long, List<Messages>> UpdateSubscribe(HashSet<long> userIds)
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
            var dynamics = GetDynamics(userId)
                .Where(d => d.PubTime > lastPubTime)
                .OrderByDescending(d => d.PubTime)
                .ToArray();
            if (dynamics.Length > 0)
            {
                var first = dynamics[0];
                Console.WriteLine(
                    $"Found new dynamic for user {userId}: {first.Title} (published at {DateTimeOffset.FromUnixTimeSeconds(first.PubTime)})"
                );
                messages.Add([new Image(first.Cover)]);
                StringBuilder sb = new();
                sb.AppendLine(
                    $"「{first.AuthorName}」于{TimeSpanToString(DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(first.PubTime))}前更新了！"
                );
                sb.AppendLine(new string('-', 30));
                sb.AppendLine($"标题：{first.Title}");
                sb.AppendLine($"简介：{first.Desc}");
                sb.AppendLine($"链接：{first.Url}");
                sb.AppendLine(new string('-', 30));
                sb.AppendLine($"截至当前数据：");
                sb.AppendLine($"- 观看：{first.Views}");
                sb.AppendLine($"- 弹幕：{first.Danmakus}");
                sb.AppendLine($"- 点赞：{first.Likes}");
                sb.AppendLine($"- 评论：{first.Comments}");
                sb.AppendLine($"- 转发：{first.Forwards}");
                messages.Add([new Text(sb.ToString().TrimEnd())]);
                Config.LastPubTimes[userId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                WriteConfig();
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
        var updates = UpdateSubscribe(userIds);
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
                        WriteConfig();
                        LoadConfig();
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
                        WriteConfig();
                        LoadConfig();
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
            var groups = Config.Subscriptions.Select(e => e.GroupId).ToArray();
            await DoCheck(groups, adapter);
        }
    }

    private Dynamic[] GetDynamics(long userId)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Config.PythonPath,
            Arguments = $"{Config.ScriptPath} --proxy {Config.Proxy} --user {userId}",
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Console.WriteLine($"Executing Python script: {startInfo.FileName} {startInfo.Arguments}");
        using Process process = Process.Start(startInfo)!;
        process.WaitForExit(TimeSpan.FromSeconds(20));
        string json = File.ReadAllText(Config.OutputPath);
        Dynamic[]? dynamics = JsonSerializer.Deserialize<Dynamic[]>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        if (dynamics is null)
        {
            Console.WriteLine("[Error] Failed to parse dynamics from Python script output.");
            return [];
        }
        return dynamics;
    }

    public override void Subscribe(IAdapter adapter)
    {
        base.Subscribe(adapter);
        RegisterSchedule("bilibili-poll", TimeSpan.FromMinutes(10), adapter);
    }
}
