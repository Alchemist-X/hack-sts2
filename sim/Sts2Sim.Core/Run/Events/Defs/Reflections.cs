namespace Sts2Sim.Core.Run;

public sealed class Reflections : EventModel
{
    public const string EventId = "REFLECTIONS";

    public const string BadLuckId = "BAD_LUCK";

    public const int DowngradeCount = 2;

    public const int UpgradeCount = 4;

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("REFLECTIONS.pages.INITIAL.options.TOUCH_A_MIRROR", TouchAMirror),
            new EventOption("REFLECTIONS.pages.INITIAL.options.SHATTER", Shatter)
        };
    }

    private void TouchAMirror(RunState rs)
    {
        var owner = Owner!;
        var rng = Rng!;
        var upgradedCards = owner.Deck.Where(c => c.IsUpgraded).ToList();
        for (int i = 0; i < DowngradeCount && upgradedCards.Count > 0; i++)
        {
            var card = rng.NextItem((IEnumerable<CardInstance>)upgradedCards)!;
            upgradedCards.Remove(card);
            CardCmd.Downgrade(rs.Combat, card);
        }
        var upgradableCards = owner.Deck.Where(c => c.IsUpgradable).ToList();
        for (int i = 0; i < UpgradeCount && upgradableCards.Count > 0; i++)
        {
            var card = rng.NextItem((IEnumerable<CardInstance>)upgradableCards)!;
            upgradableCards.Remove(card);
            CardCmd.Upgrade(rs.Combat, card);
        }
        SetEventFinished("REFLECTIONS.pages.TOUCH_A_MIRROR.description");
    }

    private void Shatter(RunState rs)
    {
        var owner = Owner!;
        int originalDeckSize = owner.Deck.Count;
        for (int i = 0; i < originalDeckSize; i++)
        {
            var card = Events121Support.CloneCard(rs, owner.Deck[i]);
            CardPileCmd.Add(rs, card, PileType.Deck);
        }
        Events121Support.AddCurseToDeck(rs, BadLuckId, owner);
        SetEventFinished("REFLECTIONS.pages.SHATTER.description");
    }
}
