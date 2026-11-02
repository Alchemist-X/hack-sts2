using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Rattle : CardDef
{
    public const string CardId = "RATTLE";
    public const int Cost = 1;
    public const decimal BaseOstyDamage = 7m;
    public const decimal CalculationBase = 0m;
    public const decimal CalculationExtra = 1m;
    public const decimal OstyDamageUpgrade = 2m;
    public const string CalculatedHitsKey = "CalculatedHits";

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> OstyAttacksThisTurn = static (cs, card, _) =>
    {
        var osty = CombatStatus.GetOsty(cs, card.Owner);
        return 1 + cs.History.CountThisTurn<CreatureAttackedEntry>(cs, e => e.IsByActor(osty));
    };

    public Rattle()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move),
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedHitsKey).WithMultiplier(OstyAttacksThisTurn)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var rattle = play.Card;
        var owner = rattle.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            var attack = DamageCmd.Attack(cs, rattle.Vars.OstyDamage.BaseValue).FromOsty(CombatStatus.GetOsty(cs, owner)!, rattle, play).Targeting(target);
            attack.WithHitCount((int)((CalculatedVar)rattle.Vars[CalculatedHitsKey]).Calculate(cs, rattle, target)).Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
    }
}
