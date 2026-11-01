namespace Sts2Sim.Core.Monsters;

public sealed class BygoneEffigyElite : EncounterDef
{
    public const string EncounterIdEntry = "BYGONE_EFFIGY_ELITE";

    public BygoneEffigyElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("BYGONE_EFFIGY"))
        };
    }
}
