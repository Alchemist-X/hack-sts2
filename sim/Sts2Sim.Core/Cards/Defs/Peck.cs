namespace Sts2Sim.Core.Cards;

public sealed class Peck : CardDef
{
    public const string CardId = "PECK";
    public const int Cost = 1;
    public const decimal BaseDamage = 2m;
    public const int BaseRepeat = 3;
    public const decimal RepeatUpgrade = 1m;

    public Peck()
        : base(CardId, Cost, CardType.Attack, CardRarity.Event, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).WithHitCount(card.Vars.Repeat.IntValue).FromCard(card, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
