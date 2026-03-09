using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Serialization;

namespace WoofBot.Sdk.Models;

public abstract class PluginBase<TConfig>(string Name, string Version, string Description) : IPlugin
    where TConfig : new()
{
    public virtual string Name { get; } = Name;
    public virtual string Version { get; } = Version;
    public virtual string Description { get; } = Description;

    protected List<IAdapter> Adapters { get; private set; } = [];
    protected bool IsEnabled { get; private set; } = false;
    protected TConfig Config { get; private set; } = new();
    protected string _configPath = "";
    protected ICronScheduler CronScheduler { get; private set; } = default!;

    public virtual void Initialize(string configDir, ICronScheduler cronScheduler)
    {
        CronScheduler = cronScheduler;
        _configPath = Path.Combine(configDir, $"{Name.ToLower()}.json");
        Config = ConfigSerializer.LoadConfig<TConfig>(_configPath);
    }

    public virtual void UpdateConfig()
    {
        ConfigSerializer.SaveConfig(_configPath, Config);
    }

    public virtual void Subscribe(IAdapter adapter)
    {
        if (!Adapters.Contains(adapter))
        {
            adapter.OnEventReceived += async (evt, _) =>
            {
                if (!IsEnabled)
                    return;
                try
                {
                    await HandleEventAsync(evt, adapter);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Error] Exception in plugin {Name} handling event: {ex.Message}"
                    );
                }
            };
            Adapters.Add(adapter);
            Console.WriteLine($"[System] Plugin {Name} subscribed to adapter {adapter.Name}");
        }
    }

    protected abstract Task HandleEventAsync(Event evt, IAdapter adapter);

    #region Cron Scheduler Convenience Methods

    /// <summary>
    /// Register a cron job owned by this plugin.
    /// </summary>
    /// <param name="name">A globally unique name for the job.</param>
    /// <param name="cronExpression">A standard 5-field cron expression.</param>
    /// <param name="callback">The async callback to execute on each occurrence.</param>
    /// <param name="maxOccurrences">Maximum executions (0 = unlimited).</param>
    protected void RegisterSchedule(
        string name,
        string cronExpression,
        Func<CancellationToken, Task> callback,
        int maxOccurrences = 0
    )
    {
        CronScheduler.Schedule(name, cronExpression, Name, callback, maxOccurrences);
    }

    /// <summary>Unregister a cron job by name.</summary>
    protected bool UnregisterSchedule(string name) => CronScheduler.Unschedule(name);

    /// <summary>Change the cron expression of an existing job.</summary>
    protected bool RescheduleTask(string name, string newCronExpression) =>
        CronScheduler.Reschedule(name, newCronExpression);

    /// <summary>Pause a running cron job.</summary>
    protected bool PauseSchedule(string name) => CronScheduler.Pause(name);

    /// <summary>Resume a paused cron job.</summary>
    protected bool ResumeSchedule(string name) => CronScheduler.Resume(name);

    /// <summary>Get all cron jobs registered by this plugin.</summary>
    protected IReadOnlyList<CronJobInfo> GetScheduledJobs() => CronScheduler.GetJobs(Name);

    #endregion

    public virtual void Enable()
    {
        IsEnabled = true;
    }

    public virtual void Disable()
    {
        IsEnabled = false;
    }

    public virtual void Dispose()
    {
        IsEnabled = false;
        // Unschedule all jobs owned by this plugin
        foreach (var job in CronScheduler.GetJobs(Name))
        {
            CronScheduler.Unschedule(job.Name);
        }
        Adapters.Clear();
    }
}
