namespace Sts2Sim.Core.Monsters;

public sealed class InkletsNormal : EncounterDef
{
    public const string EncounterIdEntry = "INKLETS_NORMAL";

    public InkletsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var inklet = MonsterRegistry.Get("INKLET");
        return new[]
        {
            new MonsterSpawn(inklet),
            new MonsterSpawn(inklet, null, EncounterMonsterConfig.Set("MiddleInklet", true)),
            new MonsterSpawn(inklet)
        };
    }
}
