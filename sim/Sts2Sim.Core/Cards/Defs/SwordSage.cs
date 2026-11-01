namespace Sts2Sim.Core.Cards;

public sealed class SwordSage : CardDef
{
    public const string CardId = "SWORD_SAGE";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const decimal BaseSwordSage = 1m;
    public const string SwordSagePowerId = "SWORD_SAGE_POWER";
    public const string SwordSagePowerVar = "SwordSagePower";

    public SwordSage()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(SwordSagePowerVar, BaseSwordSage)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var swordSage = play.Card;
        var owner = swordSage.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(SwordSagePowerId), owner, swordSage.Vars[SwordSagePowerVar].BaseValue, owner, swordSage);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
