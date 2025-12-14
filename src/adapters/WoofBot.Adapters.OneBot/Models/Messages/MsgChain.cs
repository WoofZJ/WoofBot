namespace WoofBot.Adapters.OneBot.Models.Messages;

public class MsgChain : List<MsgSegment>
{
    public MsgChain() : base() { }
    public MsgChain(string text) : base() => Add(new PlainText(text));
    public MsgChain(params MsgSegment[] segments) : base(segments) { }
    public MsgChain(IEnumerable<MsgSegment> segments) : base(segments) { }
}