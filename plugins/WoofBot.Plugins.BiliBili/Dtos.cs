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

public record YoutubeVideoInfo(
    string VideoId,
    string Title,
    string Description,
    string ChannelTitle,
    string ChannelId,
    string ChannelAvatar,
    string PublishedAt,
    string Thumbnail,
    long Duration,
    string Dimension,
    string Definition,
    bool Caption,
    int ViewCount,
    int LikeCount,
    int CommentCount,
    int LocalizationCount,
    string DefaultLanguage,
    string DefaultAudioLanguage
);

public record UserIdInfo(long UserId, string Username, long Fans);

public record NeteaseCloudArtist(long Id, string Name, string[] Aliases);

public record NeteaseCloudAlbum(long Id, string Name, long Pic, string PicUrl, string[] Aliases);

public record NeteaseCloudAudioQuality(int Bitrate, long Size, int VolumeDelta, int SampleRate);

public record NeteaseCloudQualities(
    NeteaseCloudAudioQuality? H,
    NeteaseCloudAudioQuality? M,
    NeteaseCloudAudioQuality? L,
    NeteaseCloudAudioQuality? Sq
);

public record NeteaseCloudPrivilege(
    long Id,
    int Fee,
    int Payed,
    int Status,
    int PlayLevel,
    int DownloadLevel,
    int MaxBitrate,
    int ActualBitrate,
    int PlayMaxBitrate,
    int DownloadMaxBitrate,
    bool Toast,
    int Flag
);

public record NeteaseCloudDownload(
    long Id,
    string Url,
    string Level,
    string QualityName,
    string Type,
    string EncodeType,
    long Size,
    string SizeFormatted,
    int Bitrate,
    string Md5,
    int Code,
    bool FreeTrial,
    bool Available
);

public record NeteaseCloudLyrics(
    string Lyric,
    string TranslatedLyric,
    string RomanLyric,
    string Klyric,
    string Yrc,
    bool HasLyric
);

public record NeteaseCloudSongInfo(
    long Id,
    string Name,
    string[] Aliases,
    NeteaseCloudArtist[] Artists,
    string ArtistNames,
    NeteaseCloudAlbum Album,
    string AlbumName,
    string Cover,
    int Duration,
    int DurationSeconds,
    string DurationStr,
    string Disc,
    int TrackNumber,
    int Popularity,
    long MvId,
    int Fee,
    int Copyright,
    long PublishTime,
    string PublishTimeStr,
    string CommentThreadId,
    string SourceUrl,
    NeteaseCloudQualities Qualities,
    NeteaseCloudPrivilege Privilege,
    NeteaseCloudDownload? Download,
    NeteaseCloudLyrics? Lyrics,
    string DownloadError,
    string LyricError,
    string Url,
    string Level,
    string Size,
    string Lyric,
    string Tlyric
);

public record OpusImageItem(string Url, int Width, int Height, double Size);
