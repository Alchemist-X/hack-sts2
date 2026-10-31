namespace Sts2Sim.Core.Monsters;

public sealed class SeapunkWeak : EncounterDef
{
    public const string EncounterIdEntry = "SEAPUNK_WEAK";

    public SeapunkWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[] { new MonsterSpawn(MonsterRegistry.Get(Seapunk.MonsterIdEntry)) };
    }
}
