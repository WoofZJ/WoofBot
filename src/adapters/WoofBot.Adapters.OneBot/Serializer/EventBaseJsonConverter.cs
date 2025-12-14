using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using WoofBot.Adapters.OneBot.Models.Events;
using WoofBot.Adapters.OneBot.Models.Apis;

namespace WoofBot.Adapters.OneBot.Serializer;

public class EventBaseJsonConverter : JsonConverter<EventBase>
{
    public override EventBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        if (node?["post_type"] != null)
        {
            return node?["post_type"]?.GetValue<string>() switch
            {
                "meta_event" => node?["meta_event_type"]?.GetValue<string>() switch
                {
                    "heartbeat" => node.Deserialize<HeartbeatEvent>(options),
                    "lifecycle" => node.Deserialize<LifecycleEvent>(options),
                    _ => throw new NotSupportedException("Unsupported meta_event_type")
                },
                "notice" => node?["notice_type"]?.GetValue<string>() switch
                {
                    "group_upload" => node.Deserialize<GroupUploadEvent>(options),
                    "group_admin" => node.Deserialize<GroupAdminEvent>(options),
                    "group_decrease" => node.Deserialize<GroupDecreaseEvent>(options),
                    "group_increase" => node.Deserialize<GroupIncreaseEvent>(options),
                    "group_ban" => node.Deserialize<GroupBanEvent>(options),
                    "friend_add" => node.Deserialize<FriendAddEvent>(options),
                    "group_recall" => node.Deserialize<GroupRecallEvent>(options),
                    "friend_recall" => node.Deserialize<FriendRecallEvent>(options),
                    "notify" => node?["sub_type"]?.GetValue<string>() switch
                    {
                        "poke" => node.Deserialize<PokeEvent>(options),
                        "lucky_king" => node.Deserialize<LuckyKingEvent>(options),
                        "honor" => node.Deserialize<HonorEvent>(options),
                        _ => throw new NotSupportedException("Unsupported notify sub_type")
                    },
                    _ => throw new NotSupportedException("Unsupported notice_type")
                },
                "message" => node?["message_type"]?.GetValue<string>() switch
                {
                    "private" => node.Deserialize<PrivateMessageEvent>(options),
                    "group" => node.Deserialize<GroupMessageEvent>(options),
                    _ => throw new NotSupportedException("Unsupported message_type")
                },
                "request" => node?["request_type"]?.GetValue<string>() switch
                {
                    "friend" => node.Deserialize<FriendRequestEvent>(options),
                    "group" => node.Deserialize<GroupRequestEvent>(options),
                    _ => throw new NotSupportedException("Unsupported request_type")
                },
                _ => throw new NotSupportedException("Unsupported post_type")
            };
        } else if (node?["echo"] != null)
        {
            return node?["echo"]?.GetValue<string>().Split("/")[0] switch
            {
                "send_private_msg" => node.Deserialize<ApiEvent<SendPrivateMsgData>>(options),
                "send_group_msg" => node.Deserialize<ApiEvent<SendGroupMsgData>>(options),
                "send_msg" => node.Deserialize<ApiEvent<SendMsgData>>(options),
                "delete_msg" => node.Deserialize<ApiEvent<DeleteMsgData>>(options),
                "get_msg" => node.Deserialize<ApiEvent<GetMsgData>>(options),
                "send_like" => node.Deserialize<ApiEvent<SendLikeData>>(options),
                "set_group_kick" => node.Deserialize<ApiEvent<SetGroupKickData>>(options),
                "set_group_ban" => node.Deserialize<ApiEvent<SetGroupBanData>>(options),
                "set_group_whole_ban" => node.Deserialize<ApiEvent<SetGroupWholeBanData>>(options),
                "set_group_admin" => node.Deserialize<ApiEvent<SetGroupAdminData>>(options),
                "set_group_card" => node.Deserialize<ApiEvent<SetGroupCardData>>(options),
                "set_group_name" => node.Deserialize<ApiEvent<SetGroupNameData>>(options),
                "set_group_leave" => node.Deserialize<ApiEvent<SetGroupLeaveData>>(options),
                "set_group_special_title" => node.Deserialize<ApiEvent<SetGroupSpecialTitleData>>(options),
                "set_friend_add_request" => node.Deserialize<ApiEvent<SetFriendAddRequestData>>(options),
                "set_group_add_request" => node.Deserialize<ApiEvent<SetGroupAddRequestData>>(options),
                "get_login_info" => node.Deserialize<ApiEvent<GetLoginInfoData>>(options),
                "get_stranger_info" => node.Deserialize<ApiEvent<GetStrangerInfoData>>(options),
                "get_friend_list" => node.Deserialize<ApiEvent<GetFriendListData>>(options),
                "get_group_info" => node.Deserialize<ApiEvent<GetGroupInfoData>>(options),
                "get_group_list" => node.Deserialize<ApiEvent<GetGroupListData>>(options),
                "get_group_member_info" => node.Deserialize<ApiEvent<GetGroupMemberInfoData>>(options),
                "get_group_member_list" => ((Func<EventBase?>)(() =>{
                    var list = node["data"].Deserialize<List<GetGroupMemberInfoData>>(options);
                    var data = new GetGroupMemberListData(list);
                    ApiEvent<GetGroupMemberListData> evt = new ApiEvent<GetGroupMemberListData>
                    {
                        Data = data,
                        Echo = node["echo"]!.GetValue<string>()
                    };
                    return evt;
                }))(),
                _ => throw new NotSupportedException("Unsupported API event type")
            };

        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, EventBase value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, (object)value, options);
    }
}