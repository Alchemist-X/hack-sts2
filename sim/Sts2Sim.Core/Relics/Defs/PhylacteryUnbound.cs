namespace Sts2Sim.Core.Relics;

public sealed class PhylacteryUnbound : RelicModel
{
    public const string RelicId = "PHYLACTERY_UNBOUND";

    private const string StartOfCombatKey = "StartOfCombat";

    private const string StartOfTurnKey = "StartOfTurn";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Starter;

    public override bool SpawnsPets => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(StartOfCombatKey, 5m),
        new SummonVar(StartOfTurnKey, 2m)
    };

    public override void BeforeCombatStart(RunState rs)
    {
        var cs = rs.Combat ?? throw new InvalidOperationException(RelicId + ".BeforeCombatStart without combat");
        OstyCmd.Summon(cs, Owner, DynamicVars[StartOfCombatKey].BaseValue, this);
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (participants.Any(p => ReferenceEquals(p, Owner)))
        {
            OstyCmd.Summon(cs, Owner, DynamicVars[StartOfTurnKey].BaseValue, this);
        }
    }
}
