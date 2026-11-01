using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Squeeze : CardDef
{
    public const string CardId = "SQUEEZE";
    public const int Cost = 3;
    public const decimal CalculationBase = 25m;
    public const decimal ExtraDamage = 5m;
    public const decimal CalculationBaseUpgrade = 5m;
    public const decimal ExtraDamageUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> OtherOstyAttacks = static (cs, card, _) =>
    {
        int count = 0;
        var piles = card.Owner.AllPiles;
        for (int i = 0; i < piles.Count; i++)
        {
            var cards = piles[i].Cards;
            for (int j = 0; j < cards.Count; j++)
            {
                var c = cards[j];
                if (c.HasTag(CardTag.OstyAttack) && !ReferenceEquals(c, card))
                {
                    count++;
                }
            }
        }
        return count;
    };

    public Squeeze()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(CalculationBase),
        new ExtraDamageVar(ExtraDamage).FromOsty(),
        new CalculatedDamageVar(ValueProp.Move).FromOsty().WithMultiplier(OtherOstyAttacks)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var squeeze = play.Card;
        var owner = squeeze.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            var calculatedDamage = squeeze.Vars.CalculatedDamage;
            DamageCmd.Attack(cs, (c, t) => calculatedDamage.Calculate(c, squeeze, t)).FromOsty(CombatStatus.GetOsty(cs, owner)!, squeeze, play).Targeting(target).Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
        card.Vars.ExtraDamage.UpgradeValueBy(ExtraDamageUpgrade);
    }
}
