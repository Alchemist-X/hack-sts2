namespace Sts2Sim.Core.Cards;

public sealed class TimesUp : CardDef
{
    public const string CardId = "TIMES_UP";
    public const int Cost = 2;
    public const decimal BaseCalculationBase = 0m;
    public const decimal BaseExtraDamage = 1m;
    public const string DoomPowerId = "DOOM_POWER";

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> DoomMultiplier =
        static (_, _, target) => target?.GetPower(DoomPowerId)?.Amount ?? 0;

    public TimesUp()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseCalculationBase),
        new ExtraDamageVar(BaseExtraDamage),
        new CalculatedDamageVar(ValueProp.Move).WithMultiplier(DoomMultiplier)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var timesUp = play.Card;
        DamageCmd.Attack(cs, (c, t) => timesUp.Vars.CalculatedDamage.Calculate(c, timesUp, t)).FromCard(timesUp, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
