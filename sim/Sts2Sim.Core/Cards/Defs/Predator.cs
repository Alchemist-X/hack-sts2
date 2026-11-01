namespace Sts2Sim.Core.Cards;

public sealed class Predator : CardDef
{
    public const string CardId = "PREDATOR";
    public const int Cost = 2;
    public const decimal BaseDamage = 15m;
    public const decimal DrawCardsNextTurn = 2m;
    public const decimal DamageUpgrade = 5m;
    public const string DrawCardsNextTurnPowerId = "DRAW_CARDS_NEXT_TURN_POWER";

    public Predator()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute();
        var owner = card.Owner;
        PowerCmd.Apply(cs, DrawCardsNextTurnPowerId, owner, DrawCardsNextTurn, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
