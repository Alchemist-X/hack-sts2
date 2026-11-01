using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Poke : CardDef
{
    public const string CardId = "POKE";
    public const int Cost = 0;
    public const decimal BaseOstyDamage = 6m;
    public const decimal OstyDamageUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    public Poke()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var poke = play.Card;
        var owner = poke.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            DamageCmd.Attack(cs, poke.Vars.OstyDamage.BaseValue).FromOsty(CombatStatus.GetOsty(cs, owner)!, poke, play).Targeting(target).Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
    }
}
