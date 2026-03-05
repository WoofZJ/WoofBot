using Cronos;
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
    private readonly Dictionary<string, (CronEvent, Timer)> _scheduledTasks = [];

    public virtual void Initialize(string configDir)
    {
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

    /// <summary>
    /// Register a periodic scheduled task bound to a specific adapter.
    /// The task fires a <see cref="CronEvent"/> through <see cref="HandleEventAsync"/> at each interval.
    /// </summary>
    /// <param name="name">A unique name identifying this scheduled task.</param>
    /// <param name="interval">The interval between executions.</param>
    /// <param name="adapter">The adapter this task is bound to.</param>
    /// <param name="dueTime">Optional initial delay before the first execution. Defaults to the interval value.</param>
    protected void RegisterSchedule(
        string name,
        string cron,
        IAdapter adapter,
        int occurrenceCount = 0
    )
    {
        // Prevent duplicate registrations
        if (_scheduledTasks.ContainsKey(name))
        {
            Console.WriteLine($"[Scheduler] Task '{name}' already registered, skipping.");
            return;
        }

        CronExpression cronExpr = CronExpression.Parse(cron);
        CronEvent cronEvent = new()
        {
            TaskName = name,
            Cron = cronExpr,
            CurrentOccurrence = cronExpr.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local),
            MaxOccurrences = occurrenceCount,
            OccurrenceCount = 1,
        };
        if (cronEvent.CurrentOccurrence == null)
        {
            Console.WriteLine($"[Scheduler] Invalid cron expression for task '{name}', skipping.");
            return;
        }

        var timer = new Timer(
            async _ =>
            {
                string name = cronEvent.TaskName;
                (var cronEvt, var timer) = _scheduledTasks[name];
                try
                {
                    await HandleEventAsync(cronEvt, adapter);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Scheduler] Error in task '{name}' on adapter {adapter.Name}: {ex.Message}"
                    );
                }
                if (cronEvt.MaxOccurrences > 0 && cronEvt.OccurrenceCount >= cronEvt.MaxOccurrences)
                {
                    timer.Dispose();
                    _scheduledTasks.Remove(name);
                    Console.WriteLine(
                        $"[Scheduler] Task '{name}' reached max occurrences and was removed."
                    );
                    return;
                }
                var next = cronEvt.Cron.GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.Local);
                if (next == null)
                {
                    timer.Dispose();
                    _scheduledTasks.Remove(name);
                    Console.WriteLine(
                        $"[Scheduler] No more occurrences for task '{name}', removed."
                    );
                    return;
                }
                var newCronEvent = cronEvt with
                {
                    CurrentOccurrence = next,
                    OccurrenceCount = cronEvt.OccurrenceCount + 1,
                };
                timer.Change(
                    (int)
                        newCronEvent
                            .CurrentOccurrence.Value.Subtract(DateTimeOffset.Now)
                            .TotalMilliseconds,
                    Timeout.Infinite
                );
                _scheduledTasks[name] = (newCronEvent, timer);
            },
            null,
            (int)(cronEvent.CurrentOccurrence?.Subtract(DateTimeOffset.Now).TotalMilliseconds ?? 0),
            Timeout.Infinite
        );
        _scheduledTasks[name] = (cronEvent, timer);

        Console.WriteLine(
            $"[Scheduler] Plugin {Name} registered task '{name}' on adapter {adapter.Name} with cron '{cron}'"
        );
    }

    /// <summary>
    /// Unregister a scheduled task by name. If adapter is specified, only remove for that adapter.
    /// </summary>
    protected void UnregisterSchedule(string name, IAdapter? adapter = null)
    {
        if (_scheduledTasks.TryGetValue(name, out var item))
        {
            item.Item2.Dispose();
            _scheduledTasks.Remove(name);
            Console.WriteLine($"[Scheduler] Unregistered task '{name}'");
        }
        else
        {
            Console.WriteLine($"[Scheduler] No task named '{name}' found to unregister.");
        }
    }

    private void DisposeScheduledTasks()
    {
        foreach (var (_, timer) in _scheduledTasks.Values)
        {
            timer.Dispose();
        }
        _scheduledTasks.Clear();
    }

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
        DisposeScheduledTasks();
        Adapters.Clear();
    }
}
