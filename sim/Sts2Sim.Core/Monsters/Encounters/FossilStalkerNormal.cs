namespace Sts2Sim.Core.Monsters;

public sealed class FossilStalkerNormal : EncounterDef
{
    public const string EncounterIdEntry = "FOSSIL_STALKER_NORMAL";

    public FossilStalkerNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("FOSSIL_STALKER"))
        };
    }
}
