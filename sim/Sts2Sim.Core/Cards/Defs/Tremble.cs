using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Tremble : CardDef
{
    public const string CardId = "TREMBLE";
    public const int Cost = 1;
    public const decimal BaseVulnerable = 3m;
    public const decimal VulnerableUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Tremble()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<VulnerablePower>(BaseVulnerable)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var tremble = play.Card;
        PowerCmd.Apply<VulnerablePower>(cs, target, tremble.Vars.Vulnerable.BaseValue, tremble.Owner, tremble);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Vulnerable.UpgradeValueBy(VulnerableUpgrade);
    }
}
