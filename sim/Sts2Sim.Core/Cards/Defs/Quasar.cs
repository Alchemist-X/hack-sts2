namespace Sts2Sim.Core.Cards;

public sealed class Quasar : CardDef
{
    public const string CardId = "QUASAR";
    public const int Cost = 0;
    public const int StarCost = 2;
    public const int OptionCount = 3;

    public Quasar()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var quasar = play.Card;
        var owner = quasar.Owner;
        var rs = cs.Run;
        var options = CardPools.Colorless.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        var cards = CardFactory.GetDistinctForCombat(cs, owner, options, OptionCount, rs.Rng.CombatCardGeneration).ToList();
        if (quasar.IsUpgraded)
        {
            CardCmd.Upgrade(cs, cards);
        }
        var cardModel = CardSelectCmd.FromChooseACardScreen(cs, cards, owner, canSkip: true, ChoiceSource.For(quasar, "OnPlay"));
        if (cardModel != null)
        {
            CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, owner);
        }
    }
}
