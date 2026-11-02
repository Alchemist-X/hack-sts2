namespace Sts2Sim.Core.Run;

public sealed class Amalgamator : EventModel
{
    public const string EventId = "AMALGAMATOR";

    public const string UltimateStrikeId = "ULTIMATE_STRIKE";

    public const string UltimateDefendId = "ULTIMATE_DEFEND";

    public const int CombineCount = 2;

    public override string Id => EventId;

    // Option-text vars (Models.Events/Amalgamator.cs:333 StringVar Card1 / Card2), read by the play server.
    public static string Card1 => UltimateStrikeId;

    public static string Card2 => UltimateDefendId;

    public override bool IsAllowed(RunState runState)
    {
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].Deck.Count(c => IsValid(CardTag.Strike, c)) < 2 || players[i].Deck.Count(c => IsValid(CardTag.Defend, c)) < 2)
            {
                return false;
            }
        }
        return true;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("COMBINE_STRIKES"), CombineStrikes),
        new EventOption(InitialOptionKey("COMBINE_DEFENDS"), CombineDefends)
    };

    private void CombineStrikes(RunState rs) => Combine(rs, CardTag.Strike, UltimateStrikeId, "AMALGAMATOR.pages.COMBINE_STRIKES.description", "Amalgamator.CombineStrikes");

    private void CombineDefends(RunState rs) => Combine(rs, CardTag.Defend, UltimateDefendId, "AMALGAMATOR.pages.COMBINE_DEFENDS.description", "Amalgamator.CombineDefends");

    private void Combine(RunState rs, CardTag tag, string resultCardId, string description, string siteId)
    {
        var owner = EventKit3456.OwnerOf(this);
        var cards = EventKit3456.FromDeckForRemoval(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, CombineCount), siteId, Id, c => IsValid(tag, c));
        CardPileCmd.RemoveFromDeck(rs, cards);
        CardPileCmd.Add(rs, EventKit3456.CreateCard(rs, resultCardId, owner), PileType.Deck);
        SetEventFinished(description);
    }

    private static bool IsValid(CardTag tag, CardInstance card) => card.HasTag(tag) && card.Rarity == CardRarity.Basic && card.IsRemovable;
}
