namespace Sts2Sim.Core.Cards;

public sealed class Cascade : CardDef
{
    public const string CardId = "CASCADE";
    public const int Cost = -1;

    public Cascade()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self, costsX: true)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var cascade = play.Card;
        int num = cascade.ResolveEnergyXValue(cs);
        if (cascade.IsUpgraded)
        {
            num++;
        }
        CardPileCmd.AutoPlayFromDrawPile(cs, cascade.Owner, num, CardPilePosition.Top, forceExhaust: false);
    }
}
