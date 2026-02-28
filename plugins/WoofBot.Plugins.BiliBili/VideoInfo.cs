namespace WoofBot.Plugins.BiliBili;

public record VideoInfo(
    string Bvid,
    string Title,
    string Cover,
    string Description,
    long PublishTime,
    int Duration,
    string AuthorName,
    long AuthorMid,
    string AuthorFace,
    int View,
    int Danmaku,
    int Reply,
    int Favorite,
    int Coin,
    int Share,
    int Like,
    int Dislike,
    int Width,
    int Height
);
