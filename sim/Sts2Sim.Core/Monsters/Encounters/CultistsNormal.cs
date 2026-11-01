namespace Sts2Sim.Core.Monsters;

public sealed class CultistsNormal : EncounterDef
{
    public const string EncounterIdEntry = "CULTISTS_NORMAL";

    public CultistsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("CALCIFIED_CULTIST")),
            new MonsterSpawn(MonsterRegistry.Get("DAMP_CULTIST"))
        };
    }
}
