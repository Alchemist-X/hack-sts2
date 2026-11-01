namespace Sts2Sim.Core.Monsters;

public sealed class DevotedSculptorWeak : EncounterDef
{
    public const string EncounterIdEntry = "DEVOTED_SCULPTOR_WEAK";

    public DevotedSculptorWeak()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("DEVOTED_SCULPTOR"))
        };
    }
}
