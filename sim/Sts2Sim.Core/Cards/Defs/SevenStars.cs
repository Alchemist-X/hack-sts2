namespace Sts2Sim.Core.Cards;

public sealed class SevenStars : CardDef
{
    public const string CardId = "SEVEN_STARS";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const int StarCost = 7;
    public const decimal BaseDamage = 7m;
    public const int BaseRepeat = 7;

    public SevenStars()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    public override int CanonicalStarCost => StarCost;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var sevenStars = play.Card;
        DamageCmd.Attack(cs, sevenStars.Vars.Damage.BaseValue).WithHitCount(sevenStars.Vars.Repeat.IntValue).FromCard(sevenStars, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
