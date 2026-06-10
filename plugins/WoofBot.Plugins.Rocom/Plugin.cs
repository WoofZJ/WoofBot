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
    private const int LogPreviewLength = 160;
    private const string MiniProgramUserAgent =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 MicroMessenger/8.0.49(0x18003131) NetType/WIFI Language/zh_CN miniProgram/wx0000000000000000";

    public RocomPlugin()
        : base("Rocom", "1.0", "A simple rocom plugin") { }

    private readonly HttpClient _httpClient = new();

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
                var msgs = await GetShopItemMessages();
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

    private async Task<List<Tuple<string, string>>> GetShopItems()
    {
        using var request = CreateShopItemsRequest();
        using var response = await _httpClient.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning(
                "Failed to update shop items: {StatusCode}. Response: {ResponsePreview}",
                response.StatusCode,
                GetLogPreview(html)
            );
            return [];
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var items = doc.DocumentNode.SelectNodes(
            "//div[contains(concat(' ', normalize-space(@class), ' '), ' merchant-frame-product-item ')]"
        );
        if (items is null)
        {
            Logger.LogWarning(
                "No shop item nodes found in merchant page. Response: {ResponsePreview}",
                GetLogPreview(html)
            );
            return [];
        }

        List<Tuple<string, string>> shopItems = [];
        foreach (var item in items)
        {
            var src = item.SelectSingleNode(".//img")?.GetAttributeValue("src", "")?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(src))
            {
                continue;
            }

            var time = NormalizeText(
                item.SelectSingleNode(
                    ".//div[contains(concat(' ', normalize-space(@class), ' '), ' merchant-frame-product-time ')]"
                )?.InnerText
                    ?? ""
            );
            shopItems.Add(new Tuple<string, string>(time, src));
        }
        return shopItems;
    }

    private HttpRequestMessage CreateShopItemsRequest()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Config.ApiEndpoint.TrimEnd('/')}/index.php"
        );
        request.Headers.TryAddWithoutValidation("User-Agent", MiniProgramUserAgent);
        request.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        );
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
        request.Headers.Referrer = new Uri("https://servicewechat.com/");
        request.Headers.TryAddWithoutValidation("X-Requested-With", "com.tencent.mm");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        return request;
    }

    private static string NormalizeText(string text)
    {
        return string.Join(
            " ",
            text.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
        );
    }

    private static string GetLogPreview(string text)
    {
        var preview = NormalizeText(text);
        return preview.Length <= LogPreviewLength ? preview : preview[..LogPreviewLength];
    }

    private async Task<Messages> GetShopItemMessages()
    {
        var shopItems = await GetShopItems();
        if (shopItems.Count == 0)
        {
            return [new Text($"现在远行商人已经休息啦~")];
        }
        Messages messages = [];
        messages.Add(new Text($"今日远行商人售卖商品\n"));
        shopItems
            .GroupBy(item => item.Item1)
            .ToList()
            .ForEach(group =>
            {
                messages.Add(new Text($"{group.Key} 时间段：\n"));
                group
                    .ToList()
                    .ForEach(item =>
                    {
                        messages.Add(new Image(GetShopItemImageUrl(item.Item2)));
                    });
            });
        return messages;
    }

    private string GetShopItemImageUrl(string src)
    {
        if (Uri.TryCreate(src, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return $"{Config.ApiEndpoint.TrimEnd('/')}/{src.TrimStart('/')}";
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
                var msgs = await GetShopItemMessages();
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
