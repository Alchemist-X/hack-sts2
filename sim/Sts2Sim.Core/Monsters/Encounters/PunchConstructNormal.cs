namespace Sts2Sim.Core.Monsters;

public sealed class PunchConstructNormal : EncounterDef
{
    public const string EncounterIdEntry = "PUNCH_CONSTRUCT_NORMAL";

    public PunchConstructNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("PUNCH_CONSTRUCT"))
        };
    }
}
