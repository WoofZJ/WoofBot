using Cronos;
using WoofBot.Sdk.Interfaces;

namespace WoofBot.Core;

/// <summary>
/// Global cron scheduler backed by a single <see cref="Timer"/> and a
/// <see cref="PriorityQueue{TElement,TPriority}"/> (min-heap by next fire time).
///
/// When the delay to the next job exceeds <see cref="MaxTimerDelay"/>, the timer
/// fires as a *virtual continuation* — it wakes, finds nothing due, and re-arms.
/// This avoids the int-overflow limitation of <see cref="Timer.Change(TimeSpan, TimeSpan)"/>.
///
/// Stale queue entries (from unschedule / reschedule) are skipped lazily on dequeue.
/// </summary>
public sealed class CronScheduler : ICronScheduler, IDisposable
{
    /// <summary>
    /// Safe upper bound for a single timer delay.
    /// Timer internally converts TimeSpan → int ms; values beyond ~49.7 days overflow.
    /// 12 hours gives a comfortable margin and keeps long-range jobs ticking.
    /// </summary>
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromHours(12);

    private readonly Dictionary<string, CronJobEntry> _jobs = new();
    private readonly PriorityQueue<string, DateTimeOffset> _queue = new();
    private readonly Timer _timer;
    private readonly Lock _lock = new();
    private bool _disposed;

    public CronScheduler()
    {
        _timer = new Timer(OnTimerFired, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private sealed class CronJobEntry
    {
        public required string Name { get; init; }
        public required string PluginName { get; init; }
        public required Func<CancellationToken, Task> Callback { get; init; }
        public CronExpression CronExpr { get; set; } = default!;
        public string CronExprString { get; set; } = string.Empty;
        public CancellationTokenSource Cts { get; set; } = new();
        public int MaxOccurrences { get; init; }
        public int OccurrenceCount;
        public bool IsPaused { get; set; }
        public DateTimeOffset? NextOccurrence { get; set; }
    }

    // ── Public API ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Schedule(
        string name,
        string cronExpression,
        string pluginName,
        Func<CancellationToken, Task> callback,
        int maxOccurrences = 0
    )
    {
        // CronExpression.Parse throws CronFormatException on bad input
        var cronExpr = CronExpression.Parse(cronExpression);
        var next =
            cronExpr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local)
            ?? throw new InvalidOperationException(
                $"Cron expression '{cronExpression}' yields no future occurrence."
            );

        lock (_lock)
        {
            if (_jobs.ContainsKey(name))
                throw new InvalidOperationException($"Job '{name}' already exists.");

            var entry = new CronJobEntry
            {
                Name = name,
                PluginName = pluginName,
                Callback = callback,
                CronExpr = cronExpr,
                CronExprString = cronExpression,
                MaxOccurrences = maxOccurrences,
                NextOccurrence = next,
            };

            _jobs[name] = entry;
            _queue.Enqueue(name, next);
            ArmTimer();
        }

        Console.WriteLine(
            $"[Scheduler] Registered job '{name}' for plugin '{pluginName}' "
                + $"with cron '{cronExpression}', next at {next:yyyy-MM-dd HH:mm:ss}"
        );
    }

    /// <inheritdoc />
    public bool Unschedule(string name)
    {
        lock (_lock)
        {
            if (!_jobs.Remove(name, out var entry))
                return false;

            // Signal any in-flight callback; don't Dispose here —
            // the callback may still hold a reference to the token.
            entry.Cts.Cancel();
            // Stale queue entries will be skipped lazily on dequeue.
            ArmTimer();
        }

        Console.WriteLine($"[Scheduler] Unregistered job '{name}'");
        return true;
    }

    /// <inheritdoc />
    public void Reschedule(string name, string newCronExpression)
    {
        var newCron = CronExpression.Parse(newCronExpression);
        var next =
            newCron.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local)
            ?? throw new InvalidOperationException(
                $"Cron expression '{newCronExpression}' yields no future occurrence."
            );

        lock (_lock)
        {
            if (!_jobs.TryGetValue(name, out var entry))
                throw new KeyNotFoundException($"Job '{name}' not found.");

            entry.CronExpr = newCron;
            entry.CronExprString = newCronExpression;
            entry.NextOccurrence = next;
            entry.IsPaused = false;
            // Old queue entry becomes stale; enqueue new one.
            _queue.Enqueue(name, next);
            ArmTimer();
        }

        Console.WriteLine(
            $"[Scheduler] Rescheduled job '{name}' with cron '{newCronExpression}', "
                + $"next at {next:yyyy-MM-dd HH:mm:ss}"
        );
    }

    /// <inheritdoc />
    public bool Pause(string name)
    {
        lock (_lock)
        {
            if (!_jobs.TryGetValue(name, out var entry) || entry.IsPaused)
                return false;

            entry.IsPaused = true;
            // Stale queue entries for this job will be drained by ArmTimer / skipped by OnTimerFired.
            ArmTimer();
        }

        Console.WriteLine($"[Scheduler] Paused job '{name}'");
        return true;
    }

    /// <inheritdoc />
    public bool Resume(string name)
    {
        lock (_lock)
        {
            if (!_jobs.TryGetValue(name, out var entry) || !entry.IsPaused)
                return false;

            var next = entry.CronExpr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
            if (next is null)
                return false;

            entry.NextOccurrence = next;
            entry.IsPaused = false;
            _queue.Enqueue(name, next.Value);
            ArmTimer();
        }

        Console.WriteLine($"[Scheduler] Resumed job '{name}'");
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyList<CronJobInfo> GetJobs(string? pluginName = null)
    {
        lock (_lock)
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
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;

            _timer.Dispose();
            foreach (var entry in _jobs.Values)
            {
                entry.Cts.Cancel();
                entry.Cts.Dispose();
            }
            _jobs.Clear();
        }
    }

    // ── Timer mechanics ──────────────────────────────────────────────────

    /// <summary>
    /// Set the single timer to fire at the earliest actionable job's time,
    /// capped at <see cref="MaxTimerDelay"/>.
    /// When the real delay exceeds the cap, the timer fires as a virtual
    /// continuation — wakes, finds nothing due, re-arms.
    /// Must be called under <see cref="_lock"/>.
    /// </summary>
    private void ArmTimer()
    {
        if (_disposed)
            return;

        // Drain stale / paused entries from the head so we find the real earliest job.
        while (_queue.TryPeek(out var name, out var time))
        {
            if (
                _jobs.TryGetValue(name, out var entry)
                && entry.NextOccurrence == time
                && !entry.IsPaused
            )
                break;

            _queue.Dequeue(); // stale / rescheduled / paused
        }

        if (!_queue.TryPeek(out _, out var earliest))
        {
            // No actionable jobs — park the timer.
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var delay = earliest - DateTimeOffset.Now;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        if (delay > MaxTimerDelay)
            delay = MaxTimerDelay; // virtual continuation

        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Timer callback — dequeue all due jobs and fire their callbacks.</summary>
    private void OnTimerFired(object? state)
    {
        List<CronJobEntry>? toExecute = null;

        lock (_lock)
        {
            if (_disposed)
                return;

            var now = DateTimeOffset.Now;

            while (_queue.TryPeek(out var name, out var scheduledTime))
            {
                if (scheduledTime > now)
                    break; // everything else is in the future

                _queue.Dequeue();

                if (!_jobs.TryGetValue(name, out var entry))
                    continue; // removed (stale)
                if (entry.NextOccurrence != scheduledTime)
                    continue; // rescheduled (stale)
                if (entry.IsPaused)
                    continue; // paused — skip without re-enqueue

                (toExecute ??= []).Add(entry);
            }

            ArmTimer();
        }

        if (toExecute is not null)
        {
            foreach (var entry in toExecute)
                _ = ExecuteJobAsync(entry);
        }
    }

    /// <summary>Execute a single job and schedule the next occurrence.</summary>
    private async Task ExecuteJobAsync(CronJobEntry entry)
    {
        Interlocked.Increment(ref entry.OccurrenceCount);

        try
        {
            await entry.Callback(entry.Cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scheduler] Error in job '{entry.Name}': {ex.Message}");
        }

        lock (_lock)
        {
            // Job may have been unscheduled while we were executing.
            if (!_jobs.ContainsKey(entry.Name))
                return;

            if (entry.MaxOccurrences > 0 && entry.OccurrenceCount >= entry.MaxOccurrences)
            {
                _jobs.Remove(entry.Name);
                Console.WriteLine(
                    $"[Scheduler] Job '{entry.Name}' reached max occurrences ({entry.MaxOccurrences}), removed."
                );
                return;
            }

            var next = entry.CronExpr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
            if (next is null)
            {
                _jobs.Remove(entry.Name);
                Console.WriteLine(
                    $"[Scheduler] No more occurrences for job '{entry.Name}', removed."
                );
                return;
            }

            entry.NextOccurrence = next;
            _queue.Enqueue(entry.Name, next.Value);
            ArmTimer();
        }
    }
}
