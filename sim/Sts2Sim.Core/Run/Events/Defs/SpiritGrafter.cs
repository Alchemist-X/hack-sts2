namespace Sts2Sim.Core.Run;

public sealed class SpiritGrafter : EventModel
{
    public const string EventId = "SPIRIT_GRAFTER";

    public const int RejectionHpLoss = 10;

    public const int LetItInHealAmount = 25;

    public const string MetamorphosisId = "METAMORPHOSIS";

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("SPIRIT_GRAFTER.pages.INITIAL.options.LET_IT_IN", LetItIn),
        new EventOption("SPIRIT_GRAFTER.pages.INITIAL.options.REJECTION", Rejection)
    };

    private void LetItIn(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        EventKit3456.Heal(owner, LetItInHealAmount);
        CardPileCmd.Add(rs, EventKit3456.CreateCard(rs, MetamorphosisId, owner), PileType.Deck);
        SetEventFinished("SPIRIT_GRAFTER.pages.LET_IT_IN.description");
    }

    private void Rejection(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var selected = EventKit3456.FromDeckForUpgrade(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, 1), "SpiritGrafter.Rejection", Id);
        if (selected.Count > 0)
        {
            CardCmd.Upgrade(null, selected[0]);
        }
        EventKit3456.Damage(rs, owner, RejectionHpLoss, ValueProp.Unblockable | ValueProp.Unpowered);
        SetEventFinished("SPIRIT_GRAFTER.pages.REJECTION.description");
    }
}
