namespace Sts2Sim.Core.Run;

public sealed class SunkenTreasury : EventModel
{
    public const string EventId = "SUNKEN_TREASURY";

    public const string GreedId = "GREED";

    public const decimal BaseSmallChestGold = 60m;

    public const decimal BaseLargeChestGold = 333m;

    private decimal _smallChestGold = BaseSmallChestGold;

    private decimal _largeChestGold = BaseLargeChestGold;

    public override string Id => EventId;

    public decimal SmallChestGold => _smallChestGold;

    public decimal LargeChestGold => _largeChestGold;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("SUNKEN_TREASURY.pages.INITIAL.options.FIRST_CHEST", FirstChest),
            new EventOption("SUNKEN_TREASURY.pages.INITIAL.options.SECOND_CHEST", SecondChest)
        };
    }

    public override void CalculateVars(RunState rs)
    {
        _smallChestGold += Rng!.NextInt(16) - 8;
        _largeChestGold += Rng!.NextInt(61) - 30;
    }

    private void FirstChest(RunState rs)
    {
        PlayerCmd.GainGold(rs, _smallChestGold, Owner!);
        SetEventFinished("SUNKEN_TREASURY.pages.FIRST_CHEST.description");
    }

    private void SecondChest(RunState rs)
    {
        PlayerCmd.GainGold(rs, _largeChestGold, Owner!);
        Events121Support.AddCurseToDeck(rs, GreedId, Owner!);
        SetEventFinished("SUNKEN_TREASURY.pages.SECOND_CHEST.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteDecimal(_smallChestGold);
        w.WriteDecimal(_largeChestGold);
    }
}
