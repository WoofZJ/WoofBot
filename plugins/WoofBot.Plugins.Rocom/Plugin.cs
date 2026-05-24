using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using WoofBot.Sdk.Interfaces;
using WoofBot.Sdk.Models;

namespace WoofBot.Plugins.Rocom;

public record RocomPluginConfig
{
    public List<string> Admins { get; init; } = [];
    public string ApiEndpoint { get; init; } = string.Empty;
    public List<string> EnabledGroups { get; init; } = [];
}

public class RocomPlugin : PluginBase<RocomPluginConfig>
{
    public RocomPlugin()
        : base("Rocom", "1.0", "A simple rocom plugin") { }

    private readonly HttpClient _httpClient = new();
    private readonly List<string>[] _shopItems =
    [
        [],
        [],
        [],
        [],
        [],
        [],
    ];
    private bool _updateError = false;
    private DateTime? _shopUpdateTime = null;

    public override void Initialize(string configDir, ICronScheduler cronScheduler)
    {
        base.Initialize(configDir, cronScheduler);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36 Edg/148.0.0.0"
        );
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (evt is MessageEvent msgEvt && msgEvt.Messages is [Text text])
        {
            if (text.Content == "查询远行商人" && Config.EnabledGroups.Contains(msgEvt.Target.Id))
            {
                var msgs = await GetShopItems();
                if (msgs.Count > 0)
                {
                    await adapter.SendMessageAsync(msgEvt.Target, msgs);
                }
            }
            else if (text.Content == "启用洛克查询")
            {
                if (!Config.EnabledGroups.Contains(msgEvt.Target.Id))
                {
                    Config.EnabledGroups.Add(msgEvt.Target.Id);
                    UpdateConfig();
                    await adapter.SendMessageAsync(msgEvt.Target, [new Text("已启用洛克查询~")]);
                }
                else
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("洛克查询已经启用了哦~")]
                    );
                }
            }
            else if (text.Content == "禁用洛克查询")
            {
                if (Config.EnabledGroups.Contains(msgEvt.Target.Id))
                {
                    Config.EnabledGroups.Remove(msgEvt.Target.Id);
                    UpdateConfig();
                    await adapter.SendMessageAsync(msgEvt.Target, [new Text("已禁用洛克查询~")]);
                }
                else
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("洛克查询已经禁用了哦~")]
                    );
                }
            }
            else if (
                text.Content.StartsWith("查询孵蛋")
                && Config.EnabledGroups.Contains(msgEvt.Target.Id)
            )
            {
                var slices = text.Content.Split(' ');
                if (
                    slices.Length != 3
                    || !float.TryParse(slices[1], out var size)
                    || !float.TryParse(slices[2], out var weight)
                )
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("指令格式错误，请使用：\n查询孵蛋 [尺寸] [重量]")]
                    );
                    return;
                }
                var msgs = await GetEggQueryResult(size, weight);
                await adapter.SendMessageAsync(msgEvt.Target, msgs);
            }
        }
    }

    private async Task<Messages> GetEggQueryResult(float size, float weight)
    {
        string query =
            Config.ApiEndpoint.TrimEnd('/')
            + $"/egg_group_query.php?action=predict&size={size}&weight={weight}&show_details=false&use_tongcheng=false";
        _httpClient.DefaultRequestHeaders.Add(
            "Referer",
            $"{Config.ApiEndpoint.TrimEnd("/")}/egg_group_query.php"
        );
        var request = await _httpClient.GetAsync(query);
        _httpClient.DefaultRequestHeaders.Remove("Referer");
        if (!request.IsSuccessStatusCode)
        {
            Logger.LogWarning("Failed to query egg: {StatusCode}", request.StatusCode);
            return [new Text("查询失败 ;-;")];
        }
        var json = await request.Content.ReadAsStringAsync();
        EggQueryResult? result = await request.Content.ReadFromJsonAsync<EggQueryResult>(
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }
        );
        if (result is null || result.Pokemons.Count == 0 || result.Pokemons[0].Prob < 0.01)
        {
            return [new Text("好像没有合适的精灵呢~")];
        }
        IEnumerable<Pokemon> pokemons = result
            .Pokemons.Where(p => p.Prob >= 0.01)
            .OrderByDescending(p => p.Prob);
        StringBuilder sb = new();
        sb.AppendLine($"共有 {pokemons.Count()} 个概率大于1%的结果：");
        foreach (var pokemon in pokemons)
        {
            sb.Append($"- [{pokemon.TId:D3}] ");
            sb.Append(pokemon.Name);
            sb.Append($" ({pokemon.Prob:P1})");
            if (pokemon.IsBig)
            {
                sb.Append("，会是大块头！");
            }
            else if (pokemon.IsTiny)
            {
                sb.Append("，会是小不点！");
            }
            sb.AppendLine();
        }
        return [new Text(sb.ToString().Trim())];
    }

    private async Task UpdateShopItems()
    {
        _shopUpdateTime = DateTime.UtcNow;
        int periodId = _shopUpdateTime.Value.Hour / 4;
        if (periodId >= 4)
        {
            return;
        }
        _updateError = true;
        var request = await _httpClient.GetAsync(Config.ApiEndpoint + "/index.php");
        if (!request.IsSuccessStatusCode)
        {
            Logger.LogWarning("Failed to update shop items: {StatusCode}", request.StatusCode);
            _shopItems[periodId] = [];
            return;
        }
        var html = await request.Content.ReadAsStringAsync();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var items = doc.DocumentNode.SelectNodes("//div[@class='merchant-frame-product-image']");
        List<string> srcs = [];
        foreach (var item in items)
        {
            var img = item.SelectNodes(".//img")?.First();
            if (img is not null)
            {
                var src = img.GetAttributeValue("src", "");
                srcs.Add(src);
            }
        }
        _shopItems[periodId] = srcs;
        _updateError = false;
    }

    private async Task<Messages> GetShopItems()
    {
        int periodId = DateTime.UtcNow.Hour / 4;
        if (_shopUpdateTime is null || _updateError)
        {
            await UpdateShopItems();
        }
        else
        {
            int lastUpdatePeriodId = _shopUpdateTime!.Value.Hour / 4;
            if (
                DateTime.UtcNow - _shopUpdateTime > TimeSpan.FromHours(4)
                || periodId != lastUpdatePeriodId
            )
            {
                await UpdateShopItems();
            }
        }
        if (_updateError)
        {
            return [new Text("获取远行商人商品失败 ;-;")];
        }
        int timeId = (periodId + 2) % 6;
        List<string> srcs = _shopItems[periodId];
        if (srcs.Count == 0)
        {
            return
            [
                new Text($"现在是{timeId * 4}:00~{timeId * 4 + 4}:00时间段，远行商人已经休息啦~"),
            ];
        }
        Messages messages = [];
        messages.Add(
            new Text($"现在是{timeId * 4}:00~{timeId * 4 + 4}:00时间段，远行商人售卖商品有：")
        );
        foreach (var src in srcs)
        {
            messages.Add(new Image($"{Config.ApiEndpoint.TrimEnd('/')}/{src}"));
        }
        return messages;
    }

    public override void Subscribe(IAdapter adapter)
    {
        base.Subscribe(adapter);
        RegisterSchedule(
            "rocom-check",
            "10 8,12,16,20 * * *",
            async (_) =>
            {
                if (Config.EnabledGroups.Count == 0)
                    return;
                var msgs = await GetShopItems();
                if (msgs.Count > 0)
                {
                    foreach (var groupId in Config.EnabledGroups)
                    {
                        await adapter.SendMessageAsync(new Target(TargetType.Group, groupId), msgs);
                        await Task.Delay(1000);
                    }
                }
            }
        );
    }
}
