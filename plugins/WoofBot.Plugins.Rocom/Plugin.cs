using System.Text;
using System.Text.Json;
using HtmlAgilityPack;
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

    private HttpClient _httpClient = new();

    public override void Initialize(string configDir, ICronScheduler cronScheduler)
    {
        base.Initialize(configDir, cronScheduler);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36 Edg/148.0.0.0"
        );
    }

    protected override async Task HandleEventAsync(Event evt, IAdapter adapter)
    {
        if (
            evt is MessageEvent msgEvt
            && Config.Admins.Contains(msgEvt.SenderId)
            && msgEvt.Messages is [Text text]
        )
        {
            if (text.Content == "查询远行商人")
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
                    Console.WriteLine($"Failed to query egg: {request.StatusCode}");
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("查询失败，请稍后再试~")]
                    );
                    return;
                }
                var json = await request.Content.ReadAsStringAsync();
                EggQueryResult? result = null;
                try
                {
                    result = JsonSerializer.Deserialize<EggQueryResult>(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                        }
                    );
                }
                catch { }
                if (result is null || result.Pokemons.Count == 0 || result.Pokemons[0].Prob < 0.01)
                {
                    await adapter.SendMessageAsync(
                        msgEvt.Target,
                        [new Text("好像没有合适的精灵呢~")]
                    );
                    return;
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
                await adapter.SendMessageAsync(msgEvt.Target, [new Text(sb.ToString().Trim())]);
            }
        }
    }

    private async Task<Messages> GetShopItems()
    {
        var request = await _httpClient.GetAsync(Config.ApiEndpoint + "/index.php");
        if (!request.IsSuccessStatusCode)
        {
            Console.WriteLine($"Failed to get shop items: {request.StatusCode}");
            return [];
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
        Messages messages = [];
        messages.Add(new Text("当前远行商人售卖商品："));
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
