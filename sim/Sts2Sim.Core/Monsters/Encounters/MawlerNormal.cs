namespace Sts2Sim.Core.Monsters;

public sealed class MawlerNormal : EncounterDef
{
    public const string EncounterIdEntry = "MAWLER_NORMAL";

    public MawlerNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("MAWLER"))
        };
    }
}
