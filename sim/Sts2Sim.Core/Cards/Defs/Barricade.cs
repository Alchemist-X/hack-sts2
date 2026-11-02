namespace Sts2Sim.Core.Cards;

public sealed class Barricade : CardDef
{
    public const string CardId = "BARRICADE";
    public const int Cost = 3;
    public const string BarricadePowerId = "BARRICADE_POWER";
    public const decimal BarricadeAmount = 1m;
    public const int CostUpgrade = -1;

    public Barricade()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var barricade = play.Card;
        var owner = barricade.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(BarricadePowerId), owner, BarricadeAmount, owner, barricade);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
