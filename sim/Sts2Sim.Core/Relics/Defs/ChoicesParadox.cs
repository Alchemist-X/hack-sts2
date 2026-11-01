namespace Sts2Sim.Core.Relics;

public sealed class ChoicesParadox : RelicModel
{
    public const string RelicId = "CHOICES_PARADOX";

    public const string SiteMethod = "AfterPlayerTurnStart";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(5)
    };

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber != 1)
        {
            return;
        }
        var options = CardPools.ForCharacter(Owner.CharacterId).GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run));
        var cards = CardFactory.GetDistinctForCombat(cs, Owner, options, DynamicVars.Cards.IntValue, cs.Run.Rng.CombatCardGeneration).ToList();
        if (cards.Count == 0)
        {
            return;
        }
        foreach (var card in cards)
        {
            CardCmd.ApplyKeyword(card, CardKeyword.Retain);
        }
        var prefs = new CardSelectorPrefs(RelicId + ".selectionScreenPrompt", 1);
        var selected = CardSelectCmd.FromSimpleGrid(cs, cards, Owner, prefs, ChoiceSource.For(RelicId, ChoiceSource.NoInstance, SiteMethod));
        foreach (var card in selected)
        {
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Hand, Owner);
        }
    }
}
