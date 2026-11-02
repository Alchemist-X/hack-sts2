using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class MomentumStrike : CardDef
{
    public const string CardId = "MOMENTUM_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 11m;
    public const decimal DamageUpgrade = 4m;
    public const int CostAfterPlay = 0;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public MomentumStrike()
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
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        card.EnergyCost.SetThisCombat(CostAfterPlay);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
