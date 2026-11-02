using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class UltimateStrike : CardDef
{
    public const string CardId = "ULTIMATE_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 14m;
    public const decimal DamageUpgrade = 6m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public UltimateStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
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
        var ultimateStrike = play.Card;
        DamageCmd.Attack(cs, ultimateStrike.Vars.Damage.BaseValue).FromCard(ultimateStrike, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
