using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class MeteorStrike : CardDef
{
    public const string CardId = "METEOR_STRIKE";
    public const int Cost = 5;
    public const decimal BaseDamage = 24m;
    public const decimal DamageUpgrade = 6m;
    public const int PlasmaCount = 3;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public MeteorStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var meteorStrike = play.Card;
        DamageCmd.Attack(cs, meteorStrike.Vars.Damage.BaseValue).FromCard(meteorStrike, play).Targeting(target).Execute();
        for (int i = 0; i < PlasmaCount; i++)
        {
            OrbCmd.Channel<PlasmaOrb>(cs, meteorStrike.Owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
