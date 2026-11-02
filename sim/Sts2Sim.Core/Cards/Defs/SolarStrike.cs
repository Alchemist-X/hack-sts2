using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SolarStrike : CardDef
{
    public const string CardId = "SOLAR_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const int BaseStars = 1;
    public const decimal DamageUpgrade = 1m;
    public const decimal StarsUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public SolarStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
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
        var solarStrike = play.Card;
        DamageCmd.Attack(cs, solarStrike.Vars.Damage.BaseValue).FromCard(solarStrike, play).Targeting(target).Execute();
        PlayerCmd.GainStars(cs, solarStrike.Vars.Stars.BaseValue, solarStrike.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Stars.UpgradeValueBy(StarsUpgrade);
    }
}
