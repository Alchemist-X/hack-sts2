namespace Sts2Sim.Core.Run;

public sealed class LuminousChoir : EventModel
{
    public const string EventId = "LUMINOUS_CHOIR";

    public const int BaseGold = 149;

    public const int GoldVariance = 50;

    public const int RemoveCount = 2;

    public const string SporeMindId = "SPORE_MIND";

    private int _gold = BaseGold;

    public override string Id => EventId;

    public int Gold => _gold;

    public override bool IsAllowed(RunState runState)
    {
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            var bag = players[i].RelicGrabBag;
            if (players[i].Gold < BaseGold || bag == null || !bag.HasAvailableRelics(runState))
            {
                return false;
            }
        }
        return true;
    }

    public override void CalculateVars(RunState rs)
    {
        _gold -= EventKit3456.RngOf(this).NextInt(0, GoldVariance);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var options = new List<EventOption>(1)
        {
            new EventOption("LUMINOUS_CHOIR.pages.INITIAL.options.REACH_INTO_THE_FLESH", ReachIntoTheFlesh)
        };
        if (EventKit3456.OwnerOf(this).Gold >= _gold)
        {
            options.Add(new EventOption("LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE", OfferTribute));
        }
        else
        {
            options.Add(new EventOption("LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE_LOCKED", null));
        }
        return options;
    }

    private void ReachIntoTheFlesh(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var cards = EventKit3456.FromDeckForRemoval(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, RemoveCount), "LuminousChoir.ReachIntoTheFlesh", Id);
        CardPileCmd.RemoveFromDeck(rs, cards);
        CardPileCmd.AddCurseToDeck(rs, SporeMindId, owner);
        SetEventFinished("LUMINOUS_CHOIR.pages.REACH_INTO_THE_FLESH.description");
    }

    private void OfferTribute(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        PlayerCmd.LoseGold(rs, _gold, owner, GoldLossType.Spent);
        var relic = RelicFactory.PullNextRelicFromFront(rs, owner);
        RelicCmd.Obtain(rs, relic, owner);
        SetEventFinished("LUMINOUS_CHOIR.pages.OFFER_TRIBUTE.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteInt32(_gold);
    }
}
