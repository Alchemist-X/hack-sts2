namespace Sts2Sim.Core.Relics;

public sealed class Toolbox : RelicModel
{
    public const string RelicId = "TOOLBOX";

    public const string SiteMethod = "BeforeHandDraw";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber != 1)
        {
            return;
        }
        var options = CardPools.Colorless.GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run));
        var cards = CardFactory.GetDistinctForCombat(cs, Owner, options, DynamicVars.Cards.IntValue, cs.Run.Rng.CombatCardGeneration).ToList();
        var card = CardSelectCmd.FromChooseACardScreen(cs, cards, Owner, false, ChoiceSource.For(RelicId, ChoiceSource.NoInstance, SiteMethod));
        if (card != null)
        {
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Hand, player);
        }
    }
}
