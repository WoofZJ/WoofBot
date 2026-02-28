namespace WoofBot.Plugins.BiliBili;

public record Dynamic(
    string AuthorName,
    long PubTime,
    long Forwards,
    long Comments,
    long Likes,
    string Bvid,
    string Url,
    string Cover,
    string Views,
    string Danmakus,
    string Duration,
    string Title,
    string Desc
);
