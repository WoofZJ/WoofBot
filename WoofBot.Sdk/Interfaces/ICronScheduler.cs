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
    /// Register a new cron job. The <paramref name="callback"/> is invoked each
    /// time the cron expression fires.
    /// </summary>
    /// <param name="name">A unique name for the job (must be globally unique).</param>
    /// <param name="cronExpression">A standard cron expression (5-field).</param>
    /// <param name="pluginName">The name of the owning plugin.</param>
    /// <param name="callback">The async callback to execute on each occurrence.</param>
    /// <param name="maxOccurrences">Maximum number of executions. 0 means unlimited.</param>
    void Schedule(
        string name,
        string cronExpression,
        string pluginName,
        Func<CancellationToken, Task> callback,
        int maxOccurrences = 0
    );

    /// <summary>Remove a job by name.</summary>
    bool Unschedule(string name);

    /// <summary>Change the cron expression of an existing job.</summary>
    bool Reschedule(string name, string newCronExpression);

    /// <summary>Pause a running job (timer stops but registration remains).</summary>
    bool Pause(string name);

    /// <summary>Resume a previously paused job.</summary>
    bool Resume(string name);

    /// <summary>
    /// Return information about registered jobs. Optionally filter by plugin name.
    /// </summary>
    IReadOnlyList<CronJobInfo> GetJobs(string? pluginName = null);
}
