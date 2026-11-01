using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Suppress : CardDef
{
    public const string CardId = "SUPPRESS";
    public const int Cost = 0;
    public const decimal BaseDamage = 11m;
    public const decimal BaseWeak = 3m;
    public const decimal DamageUpgrade = 6m;
    public const decimal WeakUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Innate });

    public Suppress()
        : base(CardId, Cost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<WeakPower>(BaseWeak)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var suppress = play.Card;
        DamageCmd.Attack(cs, suppress.Vars.Damage.BaseValue).FromCard(suppress, play).Targeting(target).Execute();
        PowerCmd.Apply<WeakPower>(cs, target, suppress.Vars.Weak.BaseValue, suppress.Owner, suppress);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Weak.UpgradeValueBy(WeakUpgrade);
    }
}
