namespace Sts2Sim.Core.Run;

public sealed class ColossalFlower : EventModel
{
    public const string EventId = "COLOSSAL_FLOWER";

    public const string PollinousCoreId = "POLLINOUS_CORE";

    public const int MinHp = 19;

    private static readonly int[] PrizeCosts = { 35, 75, 135 };

    private static readonly int[] PrizeDamage = { 5, 6, 7 };

    public override string Id => EventId;

    public int NumberOfDigs { get; private set; }

    public override bool IsAllowed(RunState rs) => rs.Players.All(p => p.CurrentHp >= MinHp);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey($"EXTRACT_CURRENT_PRIZE_{NumberOfDigs + 1}"), ExtractCurrentPrize),
        new EventOption(InitialOptionKey($"REACH_DEEPER_{NumberOfDigs + 1}"), ReachDeeper)
    };

    private void ReachDeeper(RunState rs)
    {
        DealReachDeeperDamage(rs);
        NumberOfDigs++;
        if (NumberOfDigs < 2)
        {
            string page = $"REACH_DEEPER_{NumberOfDigs}";
            SetEventState(Id + ".pages." + page + ".description", new[]
            {
                new EventOption(OptionKey(page, $"EXTRACT_CURRENT_PRIZE_{NumberOfDigs + 1}"), ExtractCurrentPrize),
                new EventOption(OptionKey(page, $"REACH_DEEPER_{NumberOfDigs + 1}"), ReachDeeper)
            });
        }
        else
        {
            SetEventState(Id + ".pages.REACH_DEEPER_2.description", new[]
            {
                new EventOption(OptionKey("REACH_DEEPER_2", "EXTRACT_INSTEAD"), ExtractInstead),
                new EventOption(OptionKey("REACH_DEEPER_2", "POLLINOUS_CORE"), ObtainPollinousCore)
            });
        }
    }

    private void ExtractCurrentPrize(RunState rs)
    {
        PlayerCmd.GainGold(rs, PrizeCosts[NumberOfDigs], Owner!);
        SetEventFinished(Id + ".pages.EXTRACT_CURRENT_PRIZE.description");
    }

    private void ExtractInstead(RunState rs)
    {
        PlayerCmd.GainGold(rs, PrizeCosts[NumberOfDigs], Owner!);
        SetEventFinished(Id + ".pages.EXTRACT_INSTEAD.description");
    }

    private void ObtainPollinousCore(RunState rs)
    {
        DealReachDeeperDamage(rs);
        RelicCmd.Obtain(rs, PollinousCoreId, Owner!);
        SetEventFinished(Id + ".pages.POLLINOUS_CORE.description");
    }

    private void DealReachDeeperDamage(RunState rs)
    {
        CreatureCmd.Damage(rs, Owner!, PrizeDamage[NumberOfDigs], ValueProp.Unblockable | ValueProp.Unpowered);
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(NumberOfDigs);
    }
}
