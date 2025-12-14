using WoofModels = WoofBot.Sdk.Models;

namespace WoofBot.Adapters.OneBot.Models.Messages;

public static class MsgHelper
{
    extension(MsgChain msgChain)
    {
        public WoofModels.Messages ToWoofBotMessages()
        {
            WoofModels.Messages messages = [];
            foreach (var segment in msgChain)
            {
                switch (segment)
                {
                    case PlainText textSegment:
                        messages.Add(new WoofModels.Text(textSegment.Text));
                        break;
                    case ImageRecv imageRecvSegment:
                        messages.Add(new WoofModels.ImageRecv(
                            imageRecvSegment.File, imageRecvSegment.Url, imageRecvSegment.FileSize));
                        break;
                }
            }
            return messages;
        }
    }
    extension(WoofModels.Messages messages)
    {
        public MsgChain ToOneBotMsgChain()
        {
            MsgChain msgChain = [];
            foreach (var message in messages)
            {
                switch (message)
                {
                    case WoofModels.Text textMessage:
                        msgChain.Add(new PlainText(textMessage.Content));
                        break;
                    case WoofModels.Image imageMessage:
                        msgChain.Add(new Image(imageMessage.File));
                        break;
                }
            }
            return msgChain;
        }
    }
}