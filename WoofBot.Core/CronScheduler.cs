using System.Collections.Concurrent;
using Cronos;
using WoofBot.Sdk.Interfaces;

namespace WoofBot.Core;

/// <summary>
/// Global cron scheduler implementation.
/// Manages all cron jobs centrally. Each job is identified by a globally unique name.
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class CronScheduler : ICronScheduler, IDisposable
{
    private readonly ConcurrentDictionary<string, CronJobEntry> _jobs = new();

    private sealed class CronJobEntry
    {
        public required string Name { get; init; }
        public required string PluginName { get; init; }
        public required Func<CancellationToken, Task> Callback { get; init; }
        public CronExpression CronExpr { get; set; } = default!;
        public string CronExprString { get; set; } = string.Empty;
        public Timer Timer { get; set; } = default!;
        public CancellationTokenSource Cts { get; set; } = new();
        public int MaxOccurrences { get; init; }
        public int OccurrenceCount { get; set; }
        public bool IsPaused { get; set; }
        public DateTimeOffset? NextOccurrence { get; set; }
    }

    /// <inheritdoc />
    public void Schedule(
        string name,
        string cronExpression,
        string pluginName,
        Func<CancellationToken, Task> callback,
        int maxOccurrences = 0
    )
    {
        if (_jobs.ContainsKey(name))
        {
            Console.WriteLine($"[Scheduler] Job '{name}' already exists, skipping.");
            return;
        }

        var cronExpr = CronExpression.Parse(cronExpression);
        var next = cronExpr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
        if (next is null)
        {
            Console.WriteLine($"[Scheduler] No future occurrence for job '{name}', skipping.");
            return;
        }

        var entry = new CronJobEntry
        {
            Name = name,
            PluginName = pluginName,
            Callback = callback,
            CronExpr = cronExpr,
            CronExprString = cronExpression,
            MaxOccurrences = maxOccurrences,
            OccurrenceCount = 0,
            NextOccurrence = next,
        };

        entry.Timer = CreateTimer(entry);

        if (_jobs.TryAdd(name, entry))
        {
            Console.WriteLine(
                $"[Scheduler] Registered job '{name}' for plugin '{pluginName}' "
                    + $"with cron '{cronExpression}', next at {next:yyyy-MM-dd HH:mm:ss}"
            );
        }
    }

    /// <inheritdoc />
    public bool Unschedule(string name)
    {
        if (_jobs.TryRemove(name, out var entry))
        {
            entry.Cts.Cancel();
            entry.Timer.Dispose();
            entry.Cts.Dispose();
            Console.WriteLine($"[Scheduler] Unregistered job '{name}'");
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool Reschedule(string name, string newCronExpression)
    {
        if (!_jobs.TryGetValue(name, out var entry))
            return false;

        var newCron = CronExpression.Parse(newCronExpression);
        var next = newCron.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
        if (next is null)
            return false;

        // Stop old timer
        entry.Timer.Dispose();

        entry.CronExpr = newCron;
        entry.CronExprString = newCronExpression;
        entry.NextOccurrence = next;
        entry.IsPaused = false;
        entry.Timer = CreateTimer(entry);

        Console.WriteLine(
            $"[Scheduler] Rescheduled job '{name}' with cron '{newCronExpression}', "
                + $"next at {next:yyyy-MM-dd HH:mm:ss}"
        );
        return true;
    }

    /// <inheritdoc />
    public bool Pause(string name)
    {
        if (!_jobs.TryGetValue(name, out var entry) || entry.IsPaused)
            return false;

        entry.Timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        entry.IsPaused = true;
        Console.WriteLine($"[Scheduler] Paused job '{name}'");
        return true;
    }

    /// <inheritdoc />
    public bool Resume(string name)
    {
        if (!_jobs.TryGetValue(name, out var entry) || !entry.IsPaused)
            return false;

        var next = entry.CronExpr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
        if (next is null)
            return false;

        entry.NextOccurrence = next;
        entry.IsPaused = false;

        var delay = next.Value - DateTimeOffset.Now;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        entry.Timer.Change(delay, Timeout.InfiniteTimeSpan);
        Console.WriteLine($"[Scheduler] Resumed job '{name}', next at {next:yyyy-MM-dd HH:mm:ss}");
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<CronJobInfo> GetJobs(string? pluginName = null)
    {
        var jobs = _jobs.Values.AsEnumerable();
        if (pluginName is not null)
            jobs = jobs.Where(j => j.PluginName == pluginName);

        return jobs.Select(j => new CronJobInfo
            {
                Name = j.Name,
                CronExpression = j.CronExprString,
                PluginName = j.PluginName,
                IsPaused = j.IsPaused,
                NextOccurrence = j.NextOccurrence,
                OccurrenceCount = j.OccurrenceCount,
                MaxOccurrences = j.MaxOccurrences,
            })
            .ToList()
            .AsReadOnly();
    }

    public void Dispose()
    {
        foreach (var entry in _jobs.Values)
        {
            entry.Cts.Cancel();
            entry.Timer.Dispose();
            entry.Cts.Dispose();
        }
        _jobs.Clear();
    }

    private Timer CreateTimer(CronJobEntry entry)
    {
        var delay = entry.NextOccurrence!.Value - DateTimeOffset.Now;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        return new Timer(
            async _ =>
            {
                if (entry.IsPaused)
                    return;

                entry.OccurrenceCount++;

                try
                {
                    await entry.Callback(entry.Cts.Token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Scheduler] Error in job '{entry.Name}': {ex.Message}");
                }

                // Check max occurrences
                if (entry.MaxOccurrences > 0 && entry.OccurrenceCount >= entry.MaxOccurrences)
                {
                    if (_jobs.TryRemove(entry.Name, out CronJobEntry? _))
                    {
                        entry.Timer.Dispose();
                        Console.WriteLine(
                            $"[Scheduler] Job '{entry.Name}' reached max occurrences ({entry.MaxOccurrences}), removed."
                        );
                    }
                    return;
                }

                // Schedule next occurrence
                var next = entry.CronExpr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
                if (next is null)
                {
                    if (_jobs.TryRemove(entry.Name, out CronJobEntry? _))
                    {
                        entry.Timer.Dispose();
                        Console.WriteLine(
                            $"[Scheduler] No more occurrences for job '{entry.Name}', removed."
                        );
                    }
                    return;
                }

                entry.NextOccurrence = next;
                var nextDelay = next.Value - DateTimeOffset.Now;
                if (nextDelay < TimeSpan.Zero)
                    nextDelay = TimeSpan.Zero;

                entry.Timer.Change(nextDelay, Timeout.InfiniteTimeSpan);
            },
            null,
            delay,
            Timeout.InfiniteTimeSpan
        );
    }
}
