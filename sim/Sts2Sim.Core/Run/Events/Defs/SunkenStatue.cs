namespace Sts2Sim.Core.Run;

public sealed class SunkenStatue : EventModel
{
    public const string EventId = "SUNKEN_STATUE";

    public const string SwordOfStoneId = "SWORD_OF_STONE";

    public const int BaseGold = 111;

    public const int GoldVariance = 10;

    public const decimal HpLoss = 7m;

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
            new EventOption("SUNKEN_STATUE.pages.INITIAL.options.GRAB_SWORD", GrabSword),
            new EventOption("SUNKEN_STATUE.pages.INITIAL.options.DIVE_INTO_WATER", DiveIntoWater)
        };
    }

    private void GrabSword(RunState rs)
    {
        Events121Support.ObtainRelic(rs, SwordOfStoneId, Owner!);
        SetEventFinished("SUNKEN_STATUE.pages.GRAB_SWORD.description");
    }

    private void DiveIntoWater(RunState rs)
    {
        PlayerCmd.GainGold(rs, _gold, Owner!);
        Events121Support.Damage(rs, Owner!, HpLoss, Events121Support.HpLossProps);
        SetEventFinished("SUNKEN_STATUE.pages.DIVE_INTO_WATER.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteDecimal(_gold);
    }
}
