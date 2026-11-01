namespace Sts2Sim.Core.Cards;

public sealed class Feral : CardDef
{
    public const string CardId = "FERAL";
    public const int Cost = 2;
    public const string FeralPowerKey = "FeralPower";
    public const decimal BaseFeral = 1m;
    public const int CostUpgrade = -1;
    public const string FeralPowerId = "FERAL_POWER";

    public Feral()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(FeralPowerKey, BaseFeral)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var feral = play.Card;
        var owner = feral.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(FeralPowerId), owner, feral.Vars[FeralPowerKey].BaseValue, owner, feral);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
