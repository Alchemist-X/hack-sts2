namespace Sts2Sim.Core.Run;

public sealed class ZenWeaver : EventModel
{
    public const string EventId = "ZEN_WEAVER";

    public const string EnlightenmentId = "ENLIGHTENMENT";

    public const int BreathingTechniquesCost = 50;

    public const int EmotionalAwarenessCost = 125;

    public const int ArachnidAcupunctureCost = 250;

    public const int BreathingTechniquesCards = 2;

    public const string RemovalSite = "ZEN_WEAVER:REMOVE";

    public override string Id => EventId;

    public override bool IsAllowed(RunState rs) => rs.Players.All(p => p.Gold >= EmotionalAwarenessCost);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        int gold = Owner!.Gold;
        var breathing = new EventOption(InitialOptionKey("BREATHING_TECHNIQUES"), BreathingTechniques);
        var emotional = gold < EmotionalAwarenessCost ? CreateLockedOption() : new EventOption(InitialOptionKey("EMOTIONAL_AWARENESS"), EmotionalAwareness);
        var arachnid = gold < ArachnidAcupunctureCost ? CreateLockedOption() : new EventOption(InitialOptionKey("ARACHNID_ACUPUNCTURE"), ArachnidAcupuncture);
        return new[] { breathing, emotional, arachnid };
    }

    private void BreathingTechniques(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, BreathingTechniquesCost, owner, GoldLossType.Spent);
        var cards = new CardInstance[BreathingTechniquesCards];
        for (int i = 0; i < cards.Length; i++)
        {
            cards[i] = CardFactory.CreateRunCard(rs, CardRegistry.Get(EnlightenmentId), owner);
        }
        CardPileCmd.Add(rs, cards, PileType.Deck);
        SetEventFinished(Id + ".pages.BREATHING_TECHNIQUES.description");
    }

    private void EmotionalAwareness(RunState rs)
    {
        RemoveCardsAndProceed(rs, EmotionalAwarenessCost, 1);
        SetEventFinished(Id + ".pages.EMOTIONAL_AWARENESS.description");
    }

    private void ArachnidAcupuncture(RunState rs)
    {
        RemoveCardsAndProceed(rs, ArachnidAcupunctureCost, 2);
        SetEventFinished(Id + ".pages.ARACHNID_ACUPUNCTURE.description");
    }

    private void RemoveCardsAndProceed(RunState rs, int cost, int count)
    {
        var owner = Owner!;
        var cards = CardSelectCmd.FromDeckForRemoval(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, count), new ChoiceSource(RemovalSite, Id, ChoiceSource.NoInstance)).ToList();
        CardPileCmd.RemoveFromDeck(rs, cards);
        PlayerCmd.LoseGold(rs, cost, owner, GoldLossType.Spent);
    }

    private EventOption CreateLockedOption() => new EventOption(InitialOptionKey("LOCKED"), null);
}
