namespace Sts2Sim.Core.Run;

public sealed class LostWisp : EventModel
{
    public const string EventId = "LOST_WISP";

    public const string LostWispRelicId = "LOST_WISP";

    public const string DecayId = "DECAY";

    public const int BaseGold = 60;

    public const int GoldVariance = 15;

    private decimal _gold = BaseGold;

    public override string Id => EventId;

    public decimal Gold => _gold;

    public override void CalculateVars(RunState rs)
    {
        _gold += Rng!.NextInt(-GoldVariance, GoldVariance + 1);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("LOST_WISP.pages.INITIAL.options.CLAIM", Claim),
            new EventOption("LOST_WISP.pages.INITIAL.options.SEARCH", Search)
        };
    }

    private void Claim(RunState rs)
    {
        Events121Support.AddCurseToDeck(rs, DecayId, Owner!);
        Events121Support.ObtainRelic(rs, LostWispRelicId, Owner!);
        SetEventFinished("LOST_WISP.pages.CLAIM.description");
    }

    private void Search(RunState rs)
    {
        PlayerCmd.GainGold(rs, (int)_gold, Owner!);
        SetEventFinished("LOST_WISP.pages.SEARCH.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteDecimal(_gold);
    }
}
