namespace Sts2Sim.Core.Monsters;

public sealed class LagavulinMatriarchBoss : EncounterDef
{
    public const string EncounterIdEntry = "LAGAVULIN_MATRIARCH_BOSS";

    public LagavulinMatriarchBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("LAGAVULIN_MATRIARCH"))
        };
    }
}
