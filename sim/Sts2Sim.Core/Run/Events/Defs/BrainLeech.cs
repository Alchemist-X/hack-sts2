namespace Sts2Sim.Core.Run;

public sealed class BrainLeech : EventModel
{
    public const string EventId = "BRAIN_LEECH";

    public const int RipHpLoss = 5;

    public const int RewardCount = 1;

    public const int CardChoiceCount = 1;

    public const int FromCardChoiceCount = 5;

    public const int RipRewardOptionCount = 3;

    public override string Id => EventId;

    public override bool IsAllowed(RunState runState) => runState.CurrentActIndex < 2;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("BRAIN_LEECH.pages.INITIAL.options.SHARE_KNOWLEDGE", ShareKnowledge),
        new EventOption("BRAIN_LEECH.pages.INITIAL.options.RIP", Rip)
    };

    private void Rip(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        EventKit3456.Damage(rs, owner, RipHpLoss, ValueProp.Unblockable | ValueProp.Unpowered);
        for (int i = 0; i < RewardCount; i++)
        {
            var options = CardCreationOptions.ForNonCombatWithDefaultOdds(new[] { CardPools.Colorless }).WithFlags(CardCreationFlags.NoRarityModification | CardCreationFlags.NoCardPoolModifications);
            RewardsCmd.OfferCustom(rs, owner, new List<Reward> { new CardReward(owner, options, RipRewardOptionCount) });
        }
        SetEventFinished("BRAIN_LEECH.pages.RIP.description");
    }

    private void ShareKnowledge(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var cards = CardFactory.CreateForReward(rs, owner, FromCardChoiceCount, CardCreationOptions.ForNonCombatWithDefaultOdds(new[] { CardPools.ForCharacter(owner.CharacterId) })).ToList();
        var prefs = new CardSelectorPrefs("BRAIN_LEECH.pages.SHARE_KNOWLEDGE.selectionScreenPrompt", CardChoiceCount)
        {
            Cancelable = false
        };
        EventKit3456.SelectCardsToAddToDeckFromGrid(rs, owner, cards, prefs, "BrainLeech.ShareKnowledge", Id);
        SetEventFinished("BRAIN_LEECH.pages.SHARE_KNOWLEDGE.description");
    }
}
