namespace Sts2Sim.Core.Cards;

public sealed class NoEscape : CardDef
{
    public const string CardId = "NO_ESCAPE";
    public const int Cost = 1;
    public const string CalculatedDoomKey = "CalculatedDoom";
    public const string DoomThresholdKey = "DoomThreshold";
    public const decimal DoomThreshold = 10m;
    public const decimal CalculationBase = 10m;
    public const decimal CalculationExtra = 5m;
    public const decimal CalculationBaseUpgrade = 5m;
    public const string DoomPowerId = "DOOM_POWER";

    private static readonly Func<CombatState, CardInstance, CreatureState?, decimal> DoomTiers = static (_, card, target) =>
    {
        int num = target?.GetPower(DoomPowerId)?.Amount ?? 0;
        decimal baseValue = card.Vars[DoomThresholdKey].BaseValue;
        return Math.Floor(num / baseValue);
    };

    public NoEscape()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DoomThresholdKey, DoomThreshold),
        new CalculationBaseVar(CalculationBase),
        new CalculationExtraVar(CalculationExtra),
        new CalculatedVar(CalculatedDoomKey).WithMultiplier(DoomTiers)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var noEscape = play.Card;
        decimal doom = ((CalculatedVar)noEscape.Vars[CalculatedDoomKey]).Calculate(cs, noEscape, target);
        PowerCmd.Apply(cs, PowerRegistry.Create(DoomPowerId), target, doom, noEscape.Owner, noEscape);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationBase.UpgradeValueBy(CalculationBaseUpgrade);
    }
}
