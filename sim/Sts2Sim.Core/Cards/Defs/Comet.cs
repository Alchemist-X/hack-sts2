namespace Sts2Sim.Core.Cards;

public sealed class Comet : CardDef
{
    public const string CardId = "COMET";
    public const int Cost = 0;
    public const int StarCost = 5;
    public const decimal BaseDamage = 33m;
    public const decimal BaseVulnerable = 3m;
    public const decimal BaseWeak = 3m;
    public const decimal DamageUpgrade = 11m;

    public Comet()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<VulnerablePower>(BaseVulnerable),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var comet = play.Card;
        var owner = comet.Owner;
        DamageCmd.Attack(cs, comet.Vars.Damage.BaseValue).FromCard(comet, play).Targeting(target).Execute();
        PowerCmd.Apply<WeakPower>(cs, target, comet.Vars.Weak.BaseValue, owner, comet);
        PowerCmd.Apply<VulnerablePower>(cs, target, comet.Vars.Vulnerable.BaseValue, owner, comet);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
