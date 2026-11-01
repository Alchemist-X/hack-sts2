namespace Sts2Sim.Core.Cards;

public sealed class RipAndTear : CardDef
{
    public const string CardId = "RIP_AND_TEAR";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const int HitCount = 2;
    public const decimal DamageUpgrade = 2m;

    public RipAndTear()
        : base(CardId, Cost, CardType.Attack, CardRarity.Event, TargetType.RandomEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).WithHitCount(HitCount).FromCard(card, play).TargetingRandomOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
