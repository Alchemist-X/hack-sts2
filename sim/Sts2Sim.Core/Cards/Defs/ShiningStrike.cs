using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class ShiningStrike : CardDef
{
    public const string CardId = "SHINING_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 8m;
    public const int BaseStars = 2;
    public const decimal DamageUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public ShiningStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new StarsVar(BaseStars)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var shiningStrike = play.Card;
        DamageCmd.Attack(cs, shiningStrike.Vars.Damage.BaseValue).FromCard(shiningStrike, play).Targeting(target).Execute();
        PlayerCmd.GainStars(cs, shiningStrike.Vars.Stars.BaseValue, shiningStrike.Owner);
    }

    public override CardLocation GetResultLocationForCardPlay(CombatState cs, CardInstance card)
    {
        var resultLocationForCardPlay = base.GetResultLocationForCardPlay(cs, card);
        if (resultLocationForCardPlay.PileType == PileType.Discard)
        {
            resultLocationForCardPlay = new CardLocation(PileType.Draw, CardPilePosition.Top);
        }
        return resultLocationForCardPlay;
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
