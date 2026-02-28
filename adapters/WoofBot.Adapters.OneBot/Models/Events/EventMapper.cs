using WoofBot.Adapters.OneBot.Models.Messages;
using WoofModels = WoofBot.Sdk.Models;

namespace WoofBot.Adapters.OneBot.Models.Events;

public static class EventMapper
{
    extension(OneBotEvent oneBotEvent)
    {
        public WoofModels.Event? ToWoofBotEvent()
        {
            switch (oneBotEvent)
            {
                case PrivateMessageEvent privateMessageEvent:
                    return new WoofModels.MessageEvent
                    {
                        Target = new (WoofModels.TargetType.Private, privateMessageEvent.UserId.ToString()),
                        SenderId = privateMessageEvent.Sender.UserId.ToString(),
                        Messages = privateMessageEvent.Message.ToWoofBotMessages(),
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(privateMessageEvent.Time).UtcDateTime

                    };
                case GroupMessageEvent groupMessageEvent:
                    return new WoofModels.MessageEvent
                    {
                        Target = new (WoofModels.TargetType.Group, groupMessageEvent.GroupId.ToString()),
                        SenderId = groupMessageEvent.Sender.UserId.ToString(),
                        Messages = groupMessageEvent.Message.ToWoofBotMessages(),
                        Timestamp = DateTimeOffset.FromUnixTimeSeconds(groupMessageEvent.Time).UtcDateTime
                    };
                case OneBotMetaEvent:
                    // Ignore meta events (lifecycle & heartbeat)
                    return null;
                default:
                    Console.WriteLine("Unsupported converting event type: " + oneBotEvent.GetType().Name);
                    break;
            }
            return null;
        }
    }
}