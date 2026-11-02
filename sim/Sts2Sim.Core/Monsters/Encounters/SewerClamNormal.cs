namespace Sts2Sim.Core.Monsters;

public sealed class SewerClamNormal : EncounterDef
{
    public const string EncounterIdEntry = "SEWER_CLAM_NORMAL";

    public SewerClamNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("SEWER_CLAM"))
        };
    }
}
