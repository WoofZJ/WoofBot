namespace WoofBot.Plugins.BiliBili;

public record Staff(long Mid, string Title, string Name, string Face);

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
    int Height,
    Staff[] Staffs
);

public record DouyinVideoInfo(
    string AwemeId,
    string Title,
    string Desc,
    long CreateTime,
    int Duration,
    int AwemeType,
    string Cover,
    string AuthorUid,
    string AuthorSecUid,
    string AuthorNickname,
    string AuthorAvatar,
    int Width,
    int Height,
    int DiggCount,
    int DanmakuCount,
    int CommentCount,
    int CollectCount,
    int ShareCount,
    int RecommendCount,
    string MusicTitle,
    string MusicAuthor,
    int IsTop,
    long VideoSize,
    string VideoUrl
);

public record UserIdInfo(long UserId, string Username, long Fans);
