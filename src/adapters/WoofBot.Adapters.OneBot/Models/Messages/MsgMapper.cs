using WoofBot.Sdk.Models;
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
                messages.Add(segment switch
                {
                    PlainText text =>
                        new WoofModels.Text(text.Text),
                    ImageRecv image =>
                        new WoofModels.ImageRecv(image.File, image.Url, image.FileSize),
                    At at =>
                        new WoofModels.At(at.Qq.ToString()),
                    AtAll =>
                        new WoofModels.At("all"),
                    Reply reply =>
                        new WoofModels.Reply(reply.Id),
                    _ =>
                        new UnSupportedSegment(),
                });
            }
            return messages;
        }
    }
    extension(WoofModels.Messages messages)
    {
        public MsgChain ToOneBotMsgChain()
        {
            MsgChain msgChain = [];
            foreach (var segment in messages)
            {
                msgChain.Add(segment switch
                {
                    WoofModels.Text text =>
                        new PlainText(text.Content),
                    WoofModels.Image image =>
                        new Image(image.File),
                    WoofModels.At at =>
                        at.Target == "all"
                            ? new AtAll()
                            : new At(long.Parse(at.Target)),
                    WoofModels.Reply reply =>
                        new Reply(reply.MessageId),
                    _ =>
                        new PlainText("[Unsupported Message Segment]"),
                });
            }
            return msgChain;
        }
    }
}