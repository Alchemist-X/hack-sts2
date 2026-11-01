using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class AdaptiveStrike : CardDef
{
    public const string CardId = "ADAPTIVE_STRIKE";
    public const int Cost = 2;
    public const decimal BaseDamage = 18m;
    public const decimal DamageUpgrade = 5m;
    public const int CloneCostThisCombat = 0;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public AdaptiveStrike()
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
        var adaptiveStrike = play.Card;
        DamageCmd.Attack(cs, adaptiveStrike.Vars.Damage.BaseValue).FromCard(adaptiveStrike, play).Targeting(target).Execute();
        var cardModel = cs.CloneCard(adaptiveStrike);
        cardModel.ExhaustOnNextPlay = false;
        cardModel.EnergyCost.SetThisCombat(CloneCostThisCombat);
        CardPileCmd.AddGeneratedCardToCombat(cs, cardModel, PileType.Discard, adaptiveStrike.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
