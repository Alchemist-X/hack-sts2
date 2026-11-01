namespace Sts2Sim.Core.Cards;

public sealed class SwordBoomerang : CardDef
{
    public const string CardId = "SWORD_BOOMERANG";
    public const int Cost = 1;
    public const decimal BaseDamage = 3m;
    public const int BaseRepeat = 3;
    public const decimal RepeatUpgrade = 1m;

    public SwordBoomerang()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.RandomEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var swordBoomerang = play.Card;
        DamageCmd.Attack(cs, swordBoomerang.Vars.Damage.BaseValue).WithHitCount(swordBoomerang.Vars.Repeat.IntValue).FromCard(swordBoomerang, play).TargetingRandomOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
