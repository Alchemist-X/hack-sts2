namespace Sts2Sim.Core.Cards;

public sealed class Mayhem : CardDef
{
    public const string CardId = "MAYHEM";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const decimal PowerAmount = 1m;
    public const string MayhemPowerId = "MAYHEM_POWER";

    public Mayhem()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var mayhem = play.Card;
        var owner = mayhem.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(MayhemPowerId), owner, PowerAmount, owner, mayhem);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
