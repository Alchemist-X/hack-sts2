using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class SetupStrike : CardDef
{
    public const string CardId = "SETUP_STRIKE";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const decimal BaseStrength = 3m;
    public const decimal DamageUpgrade = 2m;
    public const decimal StrengthUpgrade = 1m;
    public const string SetupStrikePowerId = "SETUP_STRIKE_POWER";

    private static readonly ReadOnlyCollection<CardTag> Tags = Array.AsReadOnly(new[] { CardTag.Strike });

    public SetupStrike()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<StrengthPower>(BaseStrength)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var owner = card.Owner;
        PowerCmd.Apply(cs, SetupStrikePowerId, owner, card.Vars.Strength.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
