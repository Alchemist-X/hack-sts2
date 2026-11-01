using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Protector : CardDef
{
    public const string CardId = "PROTECTOR";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const decimal CalculationBase = 10m;
    public const decimal ExtraDamage = 1m;
    public const decimal CalculationBaseUpgrade = 5m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> OstyMaxHp = static (cs, card, _) =>
    {
        var osty = CombatStatus.GetOsty(cs, card.Owner);
        return (osty != null && osty.IsAlive) ? osty.MaxHp : 0;
    };

    public Protector()
        : base(CardId, Cost, CardType.Attack, CardRarity.Ancient, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage).FromOsty(),
        new CalculatedDamageVar(ValueProp.Move).FromOsty().WithMultiplier(OstyMaxHp)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var protector = play.Card;
        var owner = protector.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            var calculatedDamage = protector.Vars.CalculatedDamage;
            DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, protector, t)).FromOsty(CombatStatus.GetOsty(cs, owner)!, protector, play).Targeting(target).Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
    }
}
