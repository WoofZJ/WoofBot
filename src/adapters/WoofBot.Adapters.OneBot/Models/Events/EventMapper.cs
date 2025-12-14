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
                    return new WoofModels.MessageEvent(
                        new (WoofModels.TargetType.Private, privateMessageEvent.UserId.ToString()),
                        privateMessageEvent.UserId.ToString(),
                        privateMessageEvent.Message.ToWoofBotMessages()
                    );
                case GroupMessageEvent groupMessageEvent:
                    return new WoofModels.MessageEvent(
                        new (WoofModels.TargetType.Group, groupMessageEvent.GroupId.ToString()),
                        groupMessageEvent.GroupId.ToString(),
                        groupMessageEvent.Message.ToWoofBotMessages()
                    );
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