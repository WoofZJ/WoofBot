using System.Text.Json;
using System.Collections.Concurrent;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;

namespace WoofBot.Plugins.Record;

public record RecordPluginConfig
{
    public string RecordPath { get; init; } = "records/";
}

public record RecordEntry
{
    public string SenderId { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public List<MessageSegment> Messages { get; init; } = [];
}

public class RecordPlugin : PluginBase<RecordPluginConfig>
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _groupLocks = new();

    public RecordPlugin() : base("Record", "1.0", "record group messages") {}

    public override void Initialize()
    {
        base.Initialize();
        if (!Directory.Exists(Config.RecordPath))
        {
            Directory.CreateDirectory(Config.RecordPath);
        }
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (evt is MessageEvent msgEvt
            && msgEvt.Target.Type == TargetType.Group)
        {
            var record = new RecordEntry
            {
                SenderId = msgEvt.SenderId,
                Timestamp = msgEvt.Timestamp,
                Messages = msgEvt.Messages
            };

            var dateStr = record.Timestamp.ToString("yyyy-MM-dd");
            var filePath = Path.Combine(Config.RecordPath, $"{msgEvt.Target.Id}_{dateStr}.json");

            var semaphore = _groupLocks.GetOrAdd(msgEvt.Target.Id, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                using var stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (stream.Length == 0)
                {
                    stream.WriteByte((byte)'[');
                    await JsonSerializer.SerializeAsync(stream, record);
                    stream.WriteByte((byte)']');
                }
                else
                {
                    stream.Seek(-1, SeekOrigin.End);
                    stream.WriteByte((byte)',');
                    await JsonSerializer.SerializeAsync(stream, record);
                    stream.WriteByte((byte)']');
                }
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}

