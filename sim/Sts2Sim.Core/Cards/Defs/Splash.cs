namespace Sts2Sim.Core.Cards;

public sealed class Splash : CardDef
{
    public const string CardId = "SPLASH";
    public const int Cost = 1;
    public const int OptionCount = 3;

    public Splash()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var splash = play.Card;
        var owner = splash.Owner;
        var rs = cs.Run;
        var pools = rs.Unlocks.CharacterCardPools.ToList();
        if (pools.Count > 1)
        {
            pools.Remove(CardPools.ForCharacter(owner.CharacterId));
        }
        var constraint = CardPools.MultiplayerConstraintFor(rs);
        var cards = pools.SelectMany(c => c.GetUnlockedCards(rs.Unlocks, constraint)).Where(c => c.Type == CardType.Attack);
        var options = CardFactory.GetDistinctForCombat(cs, owner, cards, OptionCount, rs.Rng.CombatCardGeneration).ToList();
        if (splash.IsUpgraded)
        {
            foreach (var current in options)
            {
                CardCmd.Upgrade(cs, current);
            }
        }
        var cardModel = CardSelectCmd.FromChooseACardScreen(cs, options, owner, canSkip: true, ChoiceSource.For(splash, "OnPlay"));
        if (cardModel != null)
        {
            cardModel.SetToFreeThisTurn();
            CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Hand, owner);
        }
    }
}
