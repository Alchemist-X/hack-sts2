namespace Sts2Sim.Core.Run;

public sealed class Wellspring : EventModel
{
    public const string EventId = "WELLSPRING";

    public const string GuiltyId = "GUILTY";

    public const int BatheCurses = 1;

    public const string BatheSite = "WELLSPRING:BATHE";

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("BOTTLE"), Bottle),
        new EventOption(InitialOptionKey("BATHE"), Bathe)
    };

    private void Bottle(RunState rs)
    {
        var owner = Owner!;
        var potion = owner.PlayerRng!.Rewards.NextItem(PotionFactory.GetPotionOptions(rs, owner));
        if (potion != null)
        {
            RewardsCmd.OfferCustom(rs, owner, new List<Reward> { new PotionReward(potion.ToMutable(), owner) });
        }
        SetEventFinished(Id + ".pages.BOTTLE.description");
    }

    private void Bathe(RunState rs)
    {
        var owner = Owner!;
        var cards = CardSelectCmd.FromDeckForRemoval(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1), new ChoiceSource(BatheSite, Id, ChoiceSource.NoInstance)).ToList();
        CardPileCmd.RemoveFromDeck(rs, cards);
        AddGuilty(rs, BatheCurses);
        SetEventFinished(Id + ".pages.BATHE.description");
    }

    private void AddGuilty(RunState rs, int amount)
    {
        CardPileCmd.AddCursesToDeck(rs, Enumerable.Repeat(RequireCard(GuiltyId), amount).ToList(), Owner!);
    }

    private static CardDef RequireCard(string cardId)
        => CardRegistry.TryGet(cardId, out var def) ? def : throw new NotImplementedException("W3 cross-owner: card " + cardId + " is not ported");
}
