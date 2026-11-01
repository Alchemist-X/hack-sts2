namespace Sts2Sim.Core.Cards;

public sealed class Conflagration : CardDef
{
    public const string CardId = "CONFLAGRATION";
    public const int Cost = 1;
    public const decimal BaseDamage = 2m;
    public const int BaseRepeat = 4;
    public const decimal RepeatUpgrade = 1m;

    public Conflagration()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).WithHitCount(card.Vars.Repeat.IntValue).FromCard(card, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
