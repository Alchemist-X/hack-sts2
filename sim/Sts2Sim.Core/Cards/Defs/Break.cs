namespace Sts2Sim.Core.Cards;

public sealed class Break : CardDef
{
    public const string CardId = "BREAK";
    public const int Cost = 1;
    public const decimal BaseDamage = 20m;
    public const decimal BaseVulnerable = 5m;
    public const decimal DamageUpgrade = 10m;
    public const decimal VulnerableUpgrade = 2m;

    public Break()
        : base(CardId, Cost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
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
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, target, card.Vars.Vulnerable.BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Vulnerable.UpgradeValueBy(VulnerableUpgrade);
    }
}
