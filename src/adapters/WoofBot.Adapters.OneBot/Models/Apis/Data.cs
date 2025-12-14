using WoofBot.Adapters.OneBot.Models.Messages;
using WoofBot.Adapters.OneBot.Models.Events;

namespace WoofBot.Adapters.OneBot.Models.Apis;

public abstract record ApiData;

public record SendPrivateMsgData(
    long MessageId
) : ApiData;

public record SendGroupMsgData(
    long MessageId
) : ApiData;

public record SendMsgData(
    long MessageId
) : ApiData;

public record DeleteMsgData() : ApiData;

public record GetMsgData(
    long Time,
    string MessageType,
    long MessageId,
    long RealId,
    GroupMessageEvent.SenderInfo Sender,
    MsgChain Message
) : ApiData;

public record SendLikeData() : ApiData;

public record SetGroupKickData() : ApiData;

public record SetGroupBanData() : ApiData;

public record SetGroupWholeBanData() : ApiData;

public record SetGroupAdminData() : ApiData;

public record SetGroupCardData() : ApiData;

public record SetGroupNameData() : ApiData;

public record SetGroupLeaveData() : ApiData;

public record SetGroupSpecialTitleData() : ApiData;

public record SetFriendAddRequestData() : ApiData;

public record SetGroupAddRequestData() : ApiData;

public record GetLoginInfoData(
    long UserId,
    string Nickname
) : ApiData;

public record GetStrangerInfoData(
    long UserId,
    string Nickname
) : ApiData;

public record FriendInfo(
    long UserId,
    string Nickname,
    string Remark
);

public record GetFriendListData(
    List<FriendInfo> Friends
) : ApiData;

public record GetGroupInfoData(
    long GroupId,
    string GroupName,
    string MemberCount,
    string MaxMemberCount
) : ApiData;

public record GetGroupListData(
    List<GetGroupInfoData> Groups
) : ApiData;

public record GetGroupMemberInfoData(
    long UserId,
    long GroupId,
    string Nickname,
    string Card,
    string CardOrNickname,
    string Sex,
    int Age,
    string Area,
    int Level,
    int QqLevel,
    long JoinTime,
    long LastSentTime,
    long TitleExpireTime,
    bool Unfriendly,
    bool CardChangeable,
    bool IsRobot,
    long ShutUpTimestamp,
    string Role,
    string Title
) : ApiData;

public record GetGroupMemberListData(
    List<GetGroupMemberInfoData> Members
) : ApiData;