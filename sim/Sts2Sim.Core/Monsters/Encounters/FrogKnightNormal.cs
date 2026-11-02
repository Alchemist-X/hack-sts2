namespace Sts2Sim.Core.Monsters;

public sealed class FrogKnightNormal : EncounterDef
{
    public const string EncounterIdEntry = "FROG_KNIGHT_NORMAL";

    public FrogKnightNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("FROG_KNIGHT"))
        };
    }
}
