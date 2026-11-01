using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class TwinStrike : CardDef
{
    public const string CardId = "TWIN_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 5m;
    public const int HitCount = 2;
    public const decimal DamageUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public TwinStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
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
        var twinStrike = play.Card;
        DamageCmd.Attack(cs, twinStrike.Vars.Damage.BaseValue).WithHitCount(HitCount).FromCard(twinStrike, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
