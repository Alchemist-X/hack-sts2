namespace Sts2Sim.Core.Cards;

public sealed class Breakthrough : CardDef
{
    public const string CardId = "BREAKTHROUGH";
    public const int Cost = 1;
    public const decimal BaseDamage = 9m;
    public const decimal BaseHpLoss = 1m;
    public const decimal DamageUpgrade = 4m;

    public Breakthrough()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new HpLossVar(BaseHpLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        CreatureCmd.Damage(cs, card.Owner, card.Vars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, play);
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
