using Milky.Net.Model;
using WoofModels = WoofBot.Sdk.Models;

namespace WoofBot.Adapters.Milky;

public static class MilkyMapper
{
    extension(IncomingSegment[] segments)
    {
        public WoofModels.Messages ToWoofBotMessages()
        {
            WoofModels.Messages messages = [];
            foreach (var segment in segments)
            {
                messages.Add(
                    segment switch
                    {
                        IncomingSegment<TextIncomingSegmentData> textSegment => new WoofModels.Text(
                            textSegment.Data.Text
                        ),
                        IncomingSegment<ImageIncomingSegmentData> imageSegment =>
                            new WoofModels.ImageRecv(
                                imageSegment.Data.ResourceId,
                                imageSegment.Data.TempUrl,
                                imageSegment.Data.Width,
                                imageSegment.Data.Height,
                                null
                            ),
                        IncomingSegment<MentionIncomingSegmentData> atSegment => new WoofModels.At(
                            atSegment.Data.UserId.ToString()
                        ),
                        IncomingSegment<MentionAllIncomingSegmentData> atAllSegment =>
                            new WoofModels.At("all"),
                        IncomingSegment<ReplyIncomingSegmentData> replySegment =>
                            new WoofModels.Reply(replySegment.Data.MessageSeq),
                        IncomingSegment<FaceIncomingSegmentData> faceSegment => new WoofModels.Face(
                            int.Parse(faceSegment.Data.FaceId)
                        ),
                        IncomingSegment<MarketFaceIncomingSegmentData> marketFaceSegment =>
                            new WoofModels.Sticker(
                                marketFaceSegment.Data.EmojiPackageId,
                                marketFaceSegment.Data.EmojiId,
                                marketFaceSegment.Data.Key
                            ),
                        _ => new WoofModels.UnSupportedSegment(),
                    }
                );
            }
            return messages;
        }
    }

    extension(WoofModels.Messages messages)
    {
        public OutgoingSegment[] ToMilkySegments()
        {
            List<OutgoingSegment> segments = [];
            foreach (var segment in messages)
            {
                segments.Add(
                    segment switch
                    {
                        WoofModels.Text text => new OutgoingSegment<TextOutgoingSegmentData>(
                            new(text.Content)
                        ),
                        WoofModels.Image image => new OutgoingSegment<ImageOutgoingSegmentData>(
                            new(new(image.File), null)
                        ),
                        WoofModels.Video video => new OutgoingSegment<VideoOutgoingSegmentData>(
                            new(new(video.File), null)
                        ),
                        WoofModels.At at => at.Target == "all"
                            ? new OutgoingSegment<MentionAllOutgoingSegmentData>(new())
                            : new OutgoingSegment<MentionOutgoingSegmentData>(
                                new(long.Parse(at.Target))
                            ),
                        WoofModels.Reply reply => new OutgoingSegment<ReplyOutgoingSegmentData>(
                            new(reply.MessageId)
                        ),
                        WoofModels.Face face => new OutgoingSegment<FaceOutgoingSegmentData>(
                            new(face.Id.ToString())
                        ),
                        _ => new OutgoingSegment<TextOutgoingSegmentData>(
                            new($"[Unsupported Message Segment: {segment.GetType().Name}]")
                        ),
                    }
                );
            }
            return segments.ToArray();
        }
    }

    extension(Event evt)
    {
        public WoofModels.Event? ToWoofBotEvent()
        {
            switch (evt)
            {
                case Event<IncomingMessage> incomingMessageEvent:
                    switch (incomingMessageEvent.Data)
                    {
                        case GroupIncomingMessage groupMsg:
                            return new WoofModels.MessageEvent
                            {
                                Target = new(
                                    WoofModels.TargetType.Group,
                                    groupMsg.Group.GroupId.ToString()
                                ),
                                SenderId = groupMsg.SenderId.ToString(),
                                Messages = groupMsg.Segments.ToWoofBotMessages(),
                            };
                        case FriendIncomingMessage privateMsg:
                            return new WoofModels.MessageEvent
                            {
                                Target = new(
                                    WoofModels.TargetType.Private,
                                    privateMsg.SenderId.ToString()
                                ),
                                SenderId = privateMsg.SenderId.ToString(),
                                Messages = privateMsg.Segments.ToWoofBotMessages(),
                            };
                        case TempIncomingMessage tempMsg:
                            return new WoofModels.MessageEvent
                            {
                                Target = new(
                                    WoofModels.TargetType.Private,
                                    tempMsg.SenderId.ToString()
                                ),
                                SenderId = tempMsg.SenderId.ToString(),
                                Messages = tempMsg.Segments.ToWoofBotMessages(),
                            };
                        default:
                            Console.WriteLine(
                                "Unsupported converting message type: "
                                    + incomingMessageEvent.Data.GetType().Name
                            );
                            break;
                    }
                    break;
                default:
                    Console.WriteLine("Unsupported converting event type: " + evt.GetType().Name);
                    break;
            }
            return null;
        }
    }
}
