namespace Sts2Sim.Core.Cards;

public sealed class Exterminate : CardDef
{
    public const string CardId = "EXTERMINATE";
    public const int Cost = 1;
    public const decimal BaseDamage = 3m;
    public const int BaseRepeat = 4;
    public const decimal DamageUpgrade = 1m;

    public Exterminate()
        : base(CardId, Cost, CardType.Attack, CardRarity.Event, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var exterminate = play.Card;
        DamageCmd.Attack(cs, exterminate.Vars.Damage.BaseValue).WithHitCount(exterminate.Vars.Repeat.IntValue).FromCard(exterminate, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
