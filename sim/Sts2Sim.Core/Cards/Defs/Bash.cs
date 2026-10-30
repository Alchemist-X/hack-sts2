namespace Sts2Sim.Core.Cards;

public sealed class Bash : CardDef
{
    public const string CardId = "BASH";
    public const int Cost = 2;
    public const decimal BaseDamage = 8m;
    public const decimal BaseVulnerable = 2m;
    public const decimal DamageUpgrade = 2m;
    public const decimal VulnerableUpgrade = 1m;

    public Bash()
        : base(CardId, Cost, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
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
        PowerCmd.Apply<VulnerablePower>(cs, target, card.Vars.Vulnerable.BaseValue, cs.Player, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Vulnerable.UpgradeValueBy(VulnerableUpgrade);
    }
}
