namespace Sts2Sim.Core.Monsters;

public sealed class SludgeSpinnerWeak : EncounterDef
{
    public const string EncounterIdEntry = "SLUDGE_SPINNER_WEAK";

    public SludgeSpinnerWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawn = new MonsterSpawn(MonsterRegistry.Get(SludgeSpinner.MonsterIdEntry), null);
        return new[] { spawn };
    }
}
