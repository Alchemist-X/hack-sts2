namespace Sts2Sim.Core.Cards;

public sealed class PrimalForce : CardDef
{
    public const string CardId = "PRIMAL_FORCE";
    public const int Cost = 0;
    public const string GiantRockCardId = "GIANT_ROCK";

    public PrimalForce()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var primalForce = play.Card;
        var owner = primalForce.Owner;
        var list = owner.Hand.Cards.Where(c => c != null && c.IsTransformable(cs) && c.Type == CardType.Attack).ToList();
        foreach (var current in list)
        {
            var cardModel = cs.CreateCard(GiantRockCardId, owner);
            if (primalForce.IsUpgraded)
            {
                CardCmd.Upgrade(cs, cardModel);
            }
            CardCmd.Transform(cs, current, cardModel);
        }
    }
}
