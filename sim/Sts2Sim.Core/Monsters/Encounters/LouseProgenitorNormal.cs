namespace Sts2Sim.Core.Monsters;

public sealed class LouseProgenitorNormal : EncounterDef
{
    public const string EncounterIdEntry = "LOUSE_PROGENITOR_NORMAL";

    public LouseProgenitorNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("LOUSE_PROGENITOR"))
        };
    }
}
