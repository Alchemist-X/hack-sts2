namespace Sts2Sim.Core.Cards;

public sealed class Debilitate : CardDef
{
    public const string CardId = "DEBILITATE";
    public const int Cost = 1;
    public const string DebilitatePowerId = "DEBILITATE_POWER";
    public const string PowerVarName = "DebilitatePower";
    public const decimal BaseDamage = 10m;
    public const decimal BaseDebilitate = 2m;
    public const decimal DamageUpgrade = 2m;
    public const decimal DebilitateUpgrade = 1m;

    public Debilitate()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new DynamicVar(PowerVarName, BaseDebilitate)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var debilitate = play.Card;
        DamageCmd.Attack(cs, debilitate.Vars.Damage.BaseValue).FromCard(debilitate, play).Targeting(target).Execute();
        PowerCmd.Apply(cs, PowerRegistry.Create(DebilitatePowerId), target, debilitate.Vars[PowerVarName].BaseValue, debilitate.Owner, debilitate);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars[PowerVarName].UpgradeValueBy(DebilitateUpgrade);
    }
}
