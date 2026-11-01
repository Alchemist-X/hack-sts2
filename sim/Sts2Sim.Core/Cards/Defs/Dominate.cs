using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Dominate : CardDef
{
    public const string CardId = "DOMINATE";
    public const int Cost = 1;
    public const string StrengthPerVulnerableKey = "StrengthPerVulnerable";
    public const decimal BaseVulnerable = 1m;
    public const decimal BaseStrengthPerVulnerable = 1m;
    public const decimal VulnerableUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Dominate()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<VulnerablePower>(BaseVulnerable),
        new DynamicVar(StrengthPerVulnerableKey, BaseStrengthPerVulnerable)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var dominate = play.Card;
        var owner = dominate.Owner;
        PowerCmd.Apply<VulnerablePower>(cs, target, dominate.Vars[DynamicVarSet.VulnerableName].BaseValue, owner, dominate);
        int vulnerable = target.GetPower<VulnerablePower>()?.Amount ?? 0;
        PowerCmd.Apply<StrengthPower>(cs, owner, vulnerable, owner, dominate);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[DynamicVarSet.VulnerableName].UpgradeValueBy(VulnerableUpgrade);
    }
}
