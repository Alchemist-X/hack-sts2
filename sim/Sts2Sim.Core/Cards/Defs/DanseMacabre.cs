namespace Sts2Sim.Core.Cards;

public sealed class DanseMacabre : CardDef
{
    public const string CardId = "DANSE_MACABRE";
    public const int Cost = 1;
    public const string DanseMacabrePowerId = "DANSE_MACABRE_POWER";
    public const string PowerVarName = "DanseMacabrePower";
    public const decimal BaseDanseMacabre = 4m;
    public const int BaseEnergy = 2;
    public const decimal DanseMacabreUpgrade = 2m;

    public DanseMacabre()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PowerVarName, BaseDanseMacabre),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var danseMacabre = play.Card;
        var owner = danseMacabre.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(DanseMacabrePowerId), owner, danseMacabre.Vars[PowerVarName].BaseValue, owner, danseMacabre);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PowerVarName].UpgradeValueBy(DanseMacabreUpgrade);
    }
}
