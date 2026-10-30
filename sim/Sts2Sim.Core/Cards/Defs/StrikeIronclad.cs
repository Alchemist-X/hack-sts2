namespace Sts2Sim.Core.Cards;

public sealed class StrikeIronclad : CardDef
{
    public const string CardId = "STRIKE_IRONCLAD";
    public const int Cost = 1;
    public const decimal BaseDamage = 6m;
    public const decimal DamageUpgrade = 3m;

    private static readonly CardTag[] Tags = { CardTag.Strike };

    public StrikeIronclad()
        : base(CardId, Cost, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
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
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
