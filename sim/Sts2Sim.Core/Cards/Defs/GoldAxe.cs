namespace Sts2Sim.Core.Cards;

public sealed class GoldAxe : CardDef
{
    public const string CardId = "GOLD_AXE";
    public const int Cost = 1;
    public const decimal BaseDamage = 0m;
    public const decimal BaseExtraDamage = 1m;

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> CardPlaysFinished = static (cs, _, _) => cs.History.CardPlaysFinished.Count;

    public GoldAxe()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseDamage),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(CardPlaysFinished)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var goldAxe = play.Card;
        DamageCmd.Attack(cs, (c, t) => goldAxe.Vars.CalculatedDamage.Calculate(c, goldAxe, t)).FromCard(goldAxe, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
