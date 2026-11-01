namespace Sts2Sim.Core.Cards;

public sealed class FallingStar : CardDef
{
    public const string CardId = "FALLING_STAR";
    public const int Cost = 0;
    public const int StarCost = 2;
    public const decimal BaseDamage = 8m;
    public const decimal BaseVulnerable = 1m;
    public const decimal BaseWeak = 1m;
    public const decimal DamageUpgrade = 4m;

    public FallingStar()
        : base(CardId, Cost, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
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
        var fallingStar = play.Card;
        var owner = fallingStar.Owner;
        DamageCmd.Attack(cs, fallingStar.Vars.Damage.BaseValue).FromCard(fallingStar, play).Targeting(target).Execute();
        PowerCmd.Apply<WeakPower>(cs, target, fallingStar.Vars.Weak.BaseValue, owner, fallingStar);
        PowerCmd.Apply<VulnerablePower>(cs, target, fallingStar.Vars.Vulnerable.BaseValue, owner, fallingStar);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
