namespace Sts2Sim.Core.Monsters;

public sealed class ConstructMenagerieNormal : EncounterDef
{
    public const string EncounterIdEntry = "CONSTRUCT_MENAGERIE_NORMAL";

    public ConstructMenagerieNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("PUNCH_CONSTRUCT")),
            new MonsterSpawn(MonsterRegistry.Get("CUBEX_CONSTRUCT")),
            new MonsterSpawn(MonsterRegistry.Get("CUBEX_CONSTRUCT"))
        };
    }
}
