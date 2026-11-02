namespace Sts2Sim.Core.Monsters;

public sealed class CubexConstructNormal : EncounterDef
{
    public const string EncounterIdEntry = "CUBEX_CONSTRUCT_NORMAL";

    public CubexConstructNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("CUBEX_CONSTRUCT"))
        };
    }
}
