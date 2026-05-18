using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;

namespace WoofBot.Plugins.Ping;

public record PingPluginConfig
{
    public List<string> Admins { get; init; } = [];
}

public class PingPlugin : PluginBase<PingPluginConfig>
{
    public PingPlugin()
        : base("Ping", "1.0", "A simple ping plugin") { }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (
            evt is MessageEvent msgEvt
            && msgEvt.Target.Type == TargetType.Group
            && Config.Admins.Contains(msgEvt.SenderId)
            && msgEvt.Messages is [Text text]
        )
        {
            if (text.Content.StartsWith("start"))
            {
                string[] parts = text.Content.Trim().Split(' ');
                if (parts.Length < 8)
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("格式：start <name> <cron> <count>")]
                    );
                    return;
                }
                string name = parts[1];
                string cron = string.Join(' ', parts.Skip(2).Take(5));
                string countStr = parts[^1];
                if (!int.TryParse(countStr, out int count))
                {
                    await adapter.SendMessageAsync(msgEvt.Target, [new Text("count 必须是整数！")]);
                    return;
                }
                try
                {
                    CronScheduler.Schedule(
                        name,
                        cron,
                        Name,
                        async (_) =>
                        {
                            CronJobInfo? jobInfo = CronScheduler
                                .GetJobs()
                                .FirstOrDefault(j => j.Name == name);
                            if (jobInfo == null)
                            {
                                await adapter.SendMessageAsync(
                                    msgEvt.Target,
                                    [new Text($"？？？定时任务 {name} 不存在了！")]
                                );
                            }
                            else
                            {
                                await adapter.SendMessageAsync(
                                    msgEvt.Target,
                                    [
                                        new Text(
                                            $"定时任务 {name} 触发了！\n已触发 {jobInfo.OccurrenceCount} 次，最大 {(jobInfo.MaxOccurrences > 0 ? jobInfo.MaxOccurrences.ToString() : "无限")} 次\n本次触发时间：{jobInfo.NextOccurrence}"
                                        ),
                                    ]
                                );
                            }
                        },
                        count
                    );
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [
                            new Text(
                                $"已创建定时任务 {name}，cron 表达式 {cron}，最大触发次数 {(count > 0 ? count.ToString() : "无限")}"
                            ),
                        ]
                    );
                }
                catch (Exception ex)
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text($"定时任务 {name} 创建失败！{ex.Message}")]
                    );
                }
            }
            else if (text.Content.StartsWith("remove"))
            {
                string name = text.Content.Substring("remove".Length).Trim();
                bool success = CronScheduler.Unschedule(name);
                await adapter.SendMessageAsync(
                    msgEvt.Target,
                    [new Text(success ? $"已停止定时任务 {name}" : $"没有找到定时任务 {name}！")]
                );
            }
            else if (text.Content.StartsWith("list"))
            {
                var jobs = CronScheduler.GetJobs();
                if (jobs.Count == 0)
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("没有正在运行的定时任务！")]
                    );
                }
                else
                {
                    string jobList = string.Join(
                        "\n",
                        jobs.Select(j =>
                            $"{j.Name}（插件:{j.PluginName}）: {j.CronExpression}, 已触发 {j.OccurrenceCount} 次，最大 {(j.MaxOccurrences > 0 ? j.MaxOccurrences.ToString() : "无限")} 次，下一次 {j.NextOccurrence}, {(j.IsPaused ? "已暂停" : "运行中")}"
                        )
                    );
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text($"正在运行的定时任务：\n{jobList}")]
                    );
                }
            }
            else if (text.Content.StartsWith("pause"))
            {
                string name = text.Content.Substring("pause".Length).Trim();
                bool success = CronScheduler.Pause(name);
                await adapter.SendMessageAsync(
                    msgEvt.Target,
                    [new Text(success ? $"已暂停定时任务 {name}" : $"没有找到定时任务 {name}！")]
                );
            }
            else if (text.Content.StartsWith("resume"))
            {
                string name = text.Content.Substring("resume".Length).Trim();
                bool success = CronScheduler.Resume(name);
                await adapter.SendMessageAsync(
                    msgEvt.Target,
                    [new Text(success ? $"已恢复定时任务 {name}" : $"没有找到定时任务 {name}！")]
                );
            }
            else if (text.Content.StartsWith("reschedule"))
            {
                string[] parts = text.Content.Trim().Split(' ');
                if (parts.Length < 7)
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("格式：reschedule <name> <new cron>")]
                    );
                    return;
                }
                string name = parts[1];
                string newCron = string.Join(' ', parts.Skip(2).Take(5));
                try
                {
                    CronScheduler.Reschedule(name, newCron);
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text($"已重新设置定时任务 {name} 为 {newCron}")]
                    );
                }
                catch (Exception ex)
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text($"重新设置失败！{ex.Message}")]
                    );
                }
            }
        }
    }
}
