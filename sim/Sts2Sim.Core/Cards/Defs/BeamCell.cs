namespace Sts2Sim.Core.Cards;

public sealed class BeamCell : CardDef
{
    public const string CardId = "BEAM_CELL";
    public const int Cost = 0;
    public const decimal BaseDamage = 3m;
    public const decimal BaseVulnerable = 1m;
    public const decimal DamageUpgrade = 1m;
    public const decimal VulnerableUpgrade = 1m;

    public BeamCell()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
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
