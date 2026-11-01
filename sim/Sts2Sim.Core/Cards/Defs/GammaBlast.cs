namespace Sts2Sim.Core.Cards;

public sealed class GammaBlast : CardDef
{
    public const string CardId = "GAMMA_BLAST";
    public const int Cost = 0;
    public const int StarCost = 3;
    public const decimal BaseDamage = 13m;
    public const decimal BaseVulnerable = 2m;
    public const decimal BaseWeak = 2m;
    public const decimal DamageUpgrade = 5m;

    public GammaBlast()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
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
        var gammaBlast = play.Card;
        var owner = gammaBlast.Owner;
        DamageCmd.Attack(cs, gammaBlast.Vars.Damage.BaseValue).FromCard(gammaBlast, play).Targeting(target).Execute();
        PowerCmd.Apply<WeakPower>(cs, target, gammaBlast.Vars.Weak.BaseValue, owner, gammaBlast);
        PowerCmd.Apply<VulnerablePower>(cs, target, gammaBlast.Vars.Vulnerable.BaseValue, owner, gammaBlast);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
