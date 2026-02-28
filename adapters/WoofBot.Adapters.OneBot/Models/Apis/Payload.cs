using WoofBot.Adapters.OneBot.Models.Messages;

namespace WoofBot.Adapters.OneBot.Models.Apis;

public abstract record ApiPayload;

public record SendPrivateMsgPayload(
    long UserId,
    MsgChain Message
) : ApiPayload;

public record SendGroupMsgPayload(
    long GroupId,
    MsgChain Message
) : ApiPayload;

public record SendMsgPayload(
    string MessageType,
    long? UserId = null,
    long? GroupId = null,
    MsgChain? Message = null
) : ApiPayload;

public record DeleteMsgPayload(
    long MessageId
) : ApiPayload;

public record GetMsgPayload(
    long MessageId
) : ApiPayload;

public record GetForwardMsgPayload(
    string Id
) : ApiPayload;

public record SendLikePayload(
    long UserId,
    int Times = 1
) : ApiPayload;

public record SetGroupKickPayload(
    long GroupId,
    long UserId,
    bool RejectAddRequest = false
) : ApiPayload;

public record SetGroupBanPayload(
    long GroupId,
    long UserId,
    int Duration = 30*60
) : ApiPayload;

public record SetGroupWholeBanPayload(
    long GroupId,
    bool Enable = true
) : ApiPayload;

public record SetGroupAdminPayload(
    long GroupId,
    long UserId,
    bool Enable = true
) : ApiPayload;

public record SetGroupCardPayload(
    long GroupId,
    long UserId,
    string Card
) : ApiPayload;

public record SetGroupNamePayload(
    long GroupId,
    string Name
) : ApiPayload;

public record SetGroupLeavePayload(
    long GroupId,
    bool IsDismiss = false
) : ApiPayload;

public record SetGroupSpecialTitlePayload(
    long GroupId,
    long UserId,
    string SpecialTitle,
    int Duration = -1
) : ApiPayload;

public record SetFriendAddRequestPayload(
    string Flag,
    bool Approve = true,
    string? Remark = null
) : ApiPayload;

public record SetGroupAddRequestPayload(
    string Flag,
    string SubType,
    bool Approve = true,
    string? Reason = null
) : ApiPayload;

public record GetLoginInfoPayload() : ApiPayload;

public record GetStrangerInfoPayload(
    long UserId,
    bool NoCache = false
) : ApiPayload;

public record GetFriendListPayload() : ApiPayload;

public record GetGroupInfoPayload(
    long GroupId,
    bool NoCache = false
) : ApiPayload;

public record GetGroupListPayload() : ApiPayload;

public record GetGroupMemberInfoPayload(
    long GroupId,
    long UserId,
    bool NoCache = false
) : ApiPayload;

public record GetGroupMemberListPayload(
    long GroupId
) : ApiPayload;