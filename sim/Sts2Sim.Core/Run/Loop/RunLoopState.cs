namespace Sts2Sim.Core.Run;

public static class RunLoopCanonicalTags
{
    public const ushort LoopState = 0x5A01;
    public const ushort Pending = 0x5A02;
    public const ushort PendingAction = 0x5A03;
    public const ushort Tape = 0x5A04;
    public const ushort Picks = 0x5A05;
    public const ushort Determinize = 0x5A06;
}

public sealed class RunLoopState : ICanonical
{
    public const int NoCardReward = -1;

    public RunPhase Phase { get; set; } = RunPhase.Map;

    public bool MapOpen { get; set; }

    public int OpenCardReward { get; set; } = NoCardReward;

    public bool Victory { get; set; }

    public RunLoopState Clone() => (RunLoopState)MemberwiseClone();

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunLoopCanonicalTags.LoopState);
        w.WriteByte((byte)Phase);
        w.WriteBool(MapOpen);
        w.WriteInt32(OpenCardReward);
        w.WriteBool(Victory);
    }
}
