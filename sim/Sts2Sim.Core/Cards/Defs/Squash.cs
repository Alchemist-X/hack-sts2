namespace Sts2Sim.Core.Cards;

public sealed class Squash : CardDef
{
    public const string CardId = "SQUASH";
    public const int Cost = 1;
    public const decimal BaseDamage = 10m;
    public const decimal BaseVulnerable = 2m;
    public const decimal DamageUpgrade = 2m;
    public const decimal VulnerableUpgrade = 1m;

    public Squash()
        : base(CardId, Cost, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<VulnerablePower>(BaseVulnerable)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var squash = play.Card;
        DamageCmd.Attack(cs, squash.Vars.Damage.BaseValue).FromCard(squash, play).Targeting(target).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, target, squash.Vars.Vulnerable.BaseValue, squash.Owner, squash);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Vulnerable.UpgradeValueBy(VulnerableUpgrade);
    }
}
