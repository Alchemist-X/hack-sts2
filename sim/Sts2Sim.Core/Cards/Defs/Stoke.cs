namespace Sts2Sim.Core.Cards;

public sealed class Stoke : CardDef
{
    public const string CardId = "STOKE";
    public const int Cost = 1;

    public Stoke()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var stoke = play.Card;
        var owner = stoke.Owner;
        var hand = owner.Hand.Cards.ToList();
        int exhaustCount = hand.Count;
        foreach (var current in hand)
        {
            CardCmd.Exhaust(cs, current);
        }
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(owner.CharacterId);
        var cards = CardFactory.GetForCombat(cs, owner, pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)), exhaustCount, rs.Rng.CombatCardGeneration).ToList();
        if (stoke.IsUpgraded)
        {
            CardCmd.Upgrade(cs, cards);
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, cards, PileType.Hand, owner);
    }
}
