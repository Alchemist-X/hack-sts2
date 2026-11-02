namespace Sts2Sim.Core.Cards;

public sealed class Synchronize : CardDef
{
    public const string CardId = "SYNCHRONIZE";
    public const int Cost = 1;
    public const decimal BaseCalculationBase = 0m;
    public const decimal BaseCalculationExtra = 1m;
    public const decimal CalculationExtraUpgrade = 1m;
    public const string CalculatedFocusKey = "CalculatedFocus";
    public const string SynchronizePowerId = "SYNCHRONIZE_POWER";

    public Synchronize()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(BaseCalculationBase),
        new CalculationExtraVar(BaseCalculationExtra),
        new CalculatedVar(CalculatedFocusKey).WithMultiplier(static (CombatState _, CardInstance card, CreatureState? _) => card.Owner.OrbQueue.Orbs.GroupBy(orb => orb.Id).Count())
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var synchronize = play.Card;
        var owner = synchronize.Owner;
        var amount = ((CalculatedVar)synchronize.Vars[CalculatedFocusKey]).Calculate(cs, synchronize, play.Target);
        PowerCmd.Apply(cs, PowerRegistry.Create(SynchronizePowerId), owner, amount, owner, synchronize);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.CalculationExtra.UpgradeValueBy(CalculationExtraUpgrade);
    }
}
