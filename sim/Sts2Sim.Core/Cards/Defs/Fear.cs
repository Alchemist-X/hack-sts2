using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Fear : CardDef
{
    public const string CardId = "FEAR";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const decimal BaseVulnerable = 1m;
    public const decimal DamageUpgrade = 1m;
    public const decimal VulnerableUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public Fear()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<VulnerablePower>(BaseVulnerable)
    };

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var fear = play.Card;
        DamageCmd.Attack(cs, fear.Vars.Damage.BaseValue).FromCard(fear, play).Targeting(target).Execute();
        PowerCmd.Apply<VulnerablePower>(cs, target, fear.Vars.Vulnerable.BaseValue, fear.Owner, fear);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Vulnerable.UpgradeValueBy(VulnerableUpgrade);
    }
}
