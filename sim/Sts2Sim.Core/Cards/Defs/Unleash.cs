using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Unleash : CardDef
{
    public const string CardId = "UNLEASH";
    public const int Cost = 1;
    public const decimal CalculationBase = 6m;
    public const decimal ExtraDamage = 1m;
    public const decimal CalculationBaseUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> OstyCurrentHp = static (cs, card, _) =>
    {
        var osty = CombatStatus.GetOsty(cs, card.Owner);
        return (osty != null && osty.IsAlive) ? osty.CurrentHp : 0;
    };

    public Unleash()
        : base(CardId, Cost, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage).FromOsty(),
        new CalculatedDamageVar(ValueProp.Move).FromOsty().WithMultiplier(OstyCurrentHp)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var unleash = play.Card;
        var owner = unleash.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            var calculatedDamage = unleash.Vars.CalculatedDamage;
            DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, unleash, t)).FromOsty(CombatStatus.GetOsty(cs, owner)!, unleash, play).Targeting(target).Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
    }
}
