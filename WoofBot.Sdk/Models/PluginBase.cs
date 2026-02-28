using System.Text.Json;
using WoofBot.Sdk.Interfaces;

namespace WoofBot.Sdk.Models;

public record ScheduledTask(string Name, TimeSpan Interval, IAdapter Adapter, Timer Timer);

public abstract class PluginBase<TConfig>(string Name, string Version, string Description) : IPlugin
    where TConfig : new()
{
    public virtual string Name { get; } = Name;
    public virtual string Version { get; } = Version;
    public virtual string Description { get; } = Description;

    protected List<IAdapter> Adapters { get; private set; } = [];
    protected bool IsEnabled { get; private set; } = false;
    protected TConfig Config { get; private set; } = new();
    private readonly List<ScheduledTask> _scheduledTasks = [];

    public virtual void WriteConfig()
    {
        var configPath = $"configs/{Name.ToLower()}.json";
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        string json = JsonSerializer.Serialize(Config, options);
        File.WriteAllText(configPath, json);
    }

    public virtual void LoadConfig()
    {
        var configPath = $"configs/{Name.ToLower()}.json";
        if (!File.Exists(configPath))
        {
            Config = new TConfig();
            WriteConfig();
        }
        try
        {
            string json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            };
            Config = JsonSerializer.Deserialize<TConfig>(json, options) ?? new TConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to load config for {Name}: {ex.Message}");
        }
    }

    public virtual void Initialize()
    {
        LoadConfig();
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
        TimeSpan interval,
        IAdapter adapter,
        TimeSpan? dueTime = null
    )
    {
        // Prevent duplicate registrations
        if (_scheduledTasks.Any(t => t.Name == name && t.Adapter == adapter))
        {
            Console.WriteLine(
                $"[Scheduler] Task '{name}' already registered for adapter {adapter.Name}, skipping."
            );
            return;
        }

        var timer = new Timer(
            async _ =>
            {
                if (!IsEnabled)
                    return;
                try
                {
                    var cronEvent = new CronEvent { TaskName = name };
                    await HandleEventAsync(cronEvent, adapter);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Scheduler] Error in task '{name}' on adapter {adapter.Name}: {ex.Message}"
                    );
                }
            },
            null,
            dueTime ?? interval,
            interval
        );

        var task = new ScheduledTask(name, interval, adapter, timer);
        _scheduledTasks.Add(task);
        Console.WriteLine(
            $"[Scheduler] Plugin {Name} registered task '{name}' every {interval} on adapter {adapter.Name}"
        );
    }

    /// <summary>
    /// Unregister a scheduled task by name. If adapter is specified, only remove for that adapter.
    /// </summary>
    protected void UnregisterSchedule(string name, IAdapter? adapter = null)
    {
        var toRemove = _scheduledTasks
            .Where(t => t.Name == name && (adapter is null || t.Adapter == adapter))
            .ToList();

        foreach (var task in toRemove)
        {
            task.Timer.Dispose();
            _scheduledTasks.Remove(task);
            Console.WriteLine(
                $"[Scheduler] Plugin {Name} unregistered task '{task.Name}' from adapter {task.Adapter.Name}"
            );
        }
    }

    private void DisposeScheduledTasks()
    {
        foreach (var task in _scheduledTasks)
        {
            task.Timer.Dispose();
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
