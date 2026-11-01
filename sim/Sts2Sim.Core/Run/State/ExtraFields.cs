namespace Sts2Sim.Core.Run;

public sealed class ExtraRunFields : ICanonical
{
    public bool StartedWithNeow { get; set; }

    public int TestSubjectKills { get; set; }

    public bool FreedRepy { get; set; }

    public ExtraRunFields DeepClone() => (ExtraRunFields)MemberwiseClone();

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.ExtraRunFields);
        w.WriteBool(StartedWithNeow);
        w.WriteInt32(TestSubjectKills);
        w.WriteBool(FreedRepy);
    }
}

public sealed class ExtraPlayerFields : ICanonical
{
    public int CardShopRemovalsUsed { get; set; }

    public int WongoPoints { get; set; }

    public ExtraPlayerFields DeepClone() => (ExtraPlayerFields)MemberwiseClone();

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.ExtraPlayerFields);
        w.WriteInt32(CardShopRemovalsUsed);
        w.WriteInt32(WongoPoints);
    }
}
