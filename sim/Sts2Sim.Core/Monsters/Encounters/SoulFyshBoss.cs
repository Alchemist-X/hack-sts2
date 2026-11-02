namespace Sts2Sim.Core.Monsters;

public sealed class SoulFyshBoss : EncounterDef
{
    public const string EncounterIdEntry = "SOUL_FYSH_BOSS";

    public SoulFyshBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("SOUL_FYSH"))
        };
    }
}
