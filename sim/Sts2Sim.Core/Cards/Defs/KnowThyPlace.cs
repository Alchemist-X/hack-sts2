using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class KnowThyPlace : CardDef
{
    public const string CardId = "KNOW_THY_PLACE";
    public const int Cost = 0;
    public const decimal BaseWeak = 1m;
    public const decimal BaseVulnerable = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public KnowThyPlace()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<WeakPower>(BaseWeak),
        new PowerVar<VulnerablePower>(BaseVulnerable)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        PowerCmd.Apply<WeakPower>(cs, target, card.Vars.Weak.BaseValue, card.Owner, card);
        PowerCmd.Apply<VulnerablePower>(cs, target, card.Vars.Vulnerable.BaseValue, card.Owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
