namespace Sts2Sim.Core.Cards;

public sealed class Havoc : CardDef
{
    public const string CardId = "HAVOC";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const int AutoPlayCount = 1;

    public Havoc()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        CardPileCmd.AutoPlayFromDrawPile(cs, play.Card.Owner, AutoPlayCount, CardPilePosition.Top, forceExhaust: true);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
