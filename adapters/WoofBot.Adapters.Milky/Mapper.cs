using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Milky.Net.Model;
using WoofBot.Sdk.Logging;
using WoofModels = WoofBot.Sdk.Models;

namespace WoofBot.Adapters.Milky;

public static class MilkyMapper
{
    private static readonly ILogger s_logger = BotLog.CreateLogger(
        typeof(MilkyMapper).FullName ?? nameof(MilkyMapper)
    );

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
                        string? appId = null,
                            title = null,
                            desc = null,
                            url = null;
                        if (node["meta"]?["news"] is not null)
                        {
                            JsonNode news = node["meta"]!["news"]!;
                            appId = news["desc"]?.GetValue<string>();
                            title = news["tag"]?.GetValue<string>();
                            desc = news["title"]?.GetValue<string>();
                            url = news["jumpUrl"]?.GetValue<string>();
                        }
                        else if (node["meta"]?["detail_1"] is not null)
                        {
                            JsonNode detail = node["meta"]!["detail_1"]!;
                            appId = detail["appid"]?.GetValue<string>();
                            title = detail["title"]?.GetValue<string>();
                            desc = detail["desc"]?.GetValue<string>();
                            url = detail["qqdocurl"]?.GetValue<string>();
                        }
                        else if (node["meta"]?["music"] is not null)
                        {
                            JsonNode music = node["meta"]!["music"]!;
                            appId = music["appid"]?.GetValue<long>().ToString();
                            title = music["tag"]?.GetValue<string>();
                            desc = music["title"]?.GetValue<string>();
                            url = music["jumpUrl"]?.GetValue<string>();
                        }
                        if (appId is null || title is null || desc is null || url is null)
                        {
                            s_logger.LogWarning(
                                "Unsupported LightApp message segment with payload: {Payload}",
                                lightAppSegment.Data.JsonPayload
                            );
                            messages.Add(new WoofModels.UnSupportedSegment());
                        }
                        else
                        {
                            messages.Add(new WoofModels.LightApp(appId, title, desc, url));
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
                if (segment is WoofModels.UploadFile uploadFile)
                {
                    continue;
                }
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
                        WoofModels.GroupedMessage grouped =>
                            new OutgoingSegment<ForwardOutgoingSegmentData>(
                                new(
                                    grouped
                                        .Pieces.Select(piece => new OutgoingForwardedMessage(
                                            long.Parse(piece.UserId),
                                            piece.Name,
                                            piece.Messages.ToMilkySegments()
                                        ))
                                        .ToArray(),
                                    grouped.Title,
                                    grouped.Preview,
                                    grouped.Summary,
                                    grouped.Prompt
                                )
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
                            s_logger.LogWarning(
                                "Unsupported converting message type: {MessageType}",
                                incomingMessageEvent.Data.GetType().Name
                            );
                            break;
                    }
                    break;
                default:
                    s_logger.LogWarning(
                        "Unsupported converting event type: {EventType}",
                        evt.GetType().Name
                    );
                    break;
            }
            return null;
        }
    }
}
