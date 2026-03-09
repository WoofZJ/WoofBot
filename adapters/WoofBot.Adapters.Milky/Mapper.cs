using System.Text.Json;
using System.Text.Json.Nodes;
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
                switch (segment)
                {
                    case IncomingSegment<TextIncomingSegmentData> textSegment:
                        messages.Add(new WoofModels.Text(textSegment.Data.Text));
                        break;
                    case IncomingSegment<ImageIncomingSegmentData> imageSegment:
                        messages.Add(
                            new WoofModels.ImageRecv(
                                imageSegment.Data.ResourceId,
                                imageSegment.Data.TempUrl,
                                imageSegment.Data.Width,
                                imageSegment.Data.Height,
                                null
                            )
                        );
                        break;
                    case IncomingSegment<MentionIncomingSegmentData> atSegment:
                        messages.Add(new WoofModels.At(atSegment.Data.UserId.ToString()));
                        break;
                    case IncomingSegment<MentionAllIncomingSegmentData> atAllSegment:
                        messages.Add(new WoofModels.At("all"));
                        break;
                    case IncomingSegment<ReplyIncomingSegmentData> replySegment:
                        messages.Add(new WoofModels.Reply(replySegment.Data.MessageSeq));
                        break;
                    case IncomingSegment<FaceIncomingSegmentData> faceSegment:
                        messages.Add(new WoofModels.Face(int.Parse(faceSegment.Data.FaceId)));
                        break;
                    case IncomingSegment<MarketFaceIncomingSegmentData> marketFaceSegment:
                        messages.Add(
                            new WoofModels.Sticker(
                                marketFaceSegment.Data.EmojiPackageId,
                                marketFaceSegment.Data.EmojiId,
                                marketFaceSegment.Data.Key
                            )
                        );
                        break;
                    case IncomingSegment<LightAppIncomingSegmentData> lightAppSegment:
                        JsonNode node = JsonSerializer.Deserialize<JsonNode>(
                            lightAppSegment.Data.JsonPayload
                        )!;
                        if (
                            node.AsObject().TryGetPropertyValue("meta", out var meta)
                            && meta is not null
                        )
                        {
                            JsonNode? appId,
                                title,
                                desc,
                                url;
                            if (
                                meta.AsObject().TryGetPropertyValue("news", out var news)
                                && news is not null
                            )
                            {
                                news.AsObject().TryGetPropertyValue("desc", out appId);
                                news.AsObject().TryGetPropertyValue("tag", out title);
                                news.AsObject().TryGetPropertyValue("title", out desc);
                                news.AsObject().TryGetPropertyValue("jumpUrl", out url);
                            }
                            else if (
                                node.AsObject().TryGetPropertyValue("detail_1", out var detail1)
                                && detail1 is not null
                            )
                            {
                                detail1.AsObject().TryGetPropertyValue("appid", out appId);
                                detail1.AsObject().TryGetPropertyValue("title", out title);
                                detail1.AsObject().TryGetPropertyValue("desc", out desc);
                                detail1.AsObject().TryGetPropertyValue("qqdocurl", out url);
                            }
                            else
                            {
                                break;
                            }
                            messages.Add(
                                new WoofModels.LightApp(
                                    appId?.GetValue<string>() ?? "",
                                    title?.GetValue<string>() ?? "",
                                    desc?.GetValue<string>() ?? "",
                                    url?.GetValue<string>() ?? ""
                                )
                            );
                        }
                        break;
                    default:
                        messages.Add(new WoofModels.UnSupportedSegment());
                        break;
                }
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
