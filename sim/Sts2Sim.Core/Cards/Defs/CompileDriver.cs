namespace Sts2Sim.Core.Cards;

public sealed class CompileDriver : CardDef
{
    public const string CardId = "COMPILE_DRIVER";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const decimal BaseCalculationBase = 0m;
    public const decimal BaseCalculationExtra = 1m;
    public const decimal DamageUpgrade = 3m;
    public const string CalculatedCardsKey = "CalculatedCards";

    public CompileDriver()
        : base(CardId, Cost, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CalculationBaseVar(BaseCalculationBase),
        new CalculationExtraVar(BaseCalculationExtra),
        new CalculatedVar(CalculatedCardsKey).WithMultiplier(static (CombatState _, CardInstance card, CreatureState? _) => card.Owner.OrbQueue.Orbs.GroupBy(orb => orb.Id).Count())
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var compileDriver = play.Card;
        DamageCmd.Attack(cs, compileDriver.Vars.Damage.BaseValue).FromCard(compileDriver, play).Targeting(target).Execute();
        CardPileCmd.Draw(cs, ((CalculatedVar)compileDriver.Vars[CalculatedCardsKey]).Calculate(cs, compileDriver, target), compileDriver.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
