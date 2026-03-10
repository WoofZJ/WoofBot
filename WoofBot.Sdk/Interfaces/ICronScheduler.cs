namespace WoofBot.Sdk.Interfaces;

/// <summary>
/// Snapshot information about a registered cron job.
/// </summary>
public record CronJobInfo
{
    public string Name { get; init; } = string.Empty;
    public string CronExpression { get; init; } = string.Empty;
    public string PluginName { get; init; } = string.Empty;
    public bool IsPaused { get; init; }
    public DateTimeOffset? NextOccurrence { get; init; }
    public int OccurrenceCount { get; init; }
    public int MaxOccurrences { get; init; }
}

/// <summary>
/// Global cron scheduler that manages periodic tasks across all plugins.
/// Plugins can schedule, unschedule, reschedule, pause and resume cron jobs.
/// </summary>
public interface ICronScheduler
{
    /// <summary>
    /// Register a new cron job. Throws on invalid cron expression or duplicate name.
    /// </summary>
    /// <exception cref="InvalidOperationException">Duplicate name or no future occurrence.</exception>
    void Schedule(
        string name,
        string cronExpression,
        string pluginName,
        Func<CancellationToken, Task> callback,
        int maxOccurrences = 0
    );

    /// <summary>Remove a job by name. Returns false if not found.</summary>
    bool Unschedule(string name);

    /// <summary>
    /// Change the cron expression of an existing job.
    /// Throws if the job is not found or the new expression is invalid.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Job not found.</exception>
    /// <exception cref="InvalidOperationException">No future occurrence.</exception>
    void Reschedule(string name, string newCronExpression);

    /// <summary>Pause a running job. Returns false if not found or already paused.</summary>
    bool Pause(string name);

    /// <summary>Resume a paused job. Returns false if not found or not paused.</summary>
    bool Resume(string name);

    /// <summary>
    /// Return information about registered jobs. Optionally filter by plugin name.
    /// </summary>
    IReadOnlyList<CronJobInfo> GetJobs(string? pluginName = null);
}
