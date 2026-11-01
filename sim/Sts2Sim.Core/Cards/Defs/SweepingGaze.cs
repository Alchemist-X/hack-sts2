using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SweepingGaze : CardDef
{
    public const string CardId = "SWEEPING_GAZE";
    public const int Cost = 0;
    public const decimal BaseOstyDamage = 10m;
    public const decimal OstyDamageUpgrade = 5m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.OstyAttack });

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal, CardKeyword.Exhaust });

    public SweepingGaze()
        : base(CardId, Cost, CardType.Attack, CardRarity.Token, TargetType.RandomEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new OstyDamageVar(BaseOstyDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var sweepingGaze = play.Card;
        var owner = sweepingGaze.Owner;
        if (!CombatStatus.IsOstyMissing(cs, owner))
        {
            DamageCmd.Attack(cs, sweepingGaze.Vars.OstyDamage.BaseValue).FromOsty(CombatStatus.GetOsty(cs, owner)!, sweepingGaze, play).TargetingRandomOpponents().Execute();
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.OstyDamage.UpgradeValueBy(OstyDamageUpgrade);
    }
}
