namespace Sts2Sim.Core.Monsters;

public sealed class MysteriousKnightEventEncounter : EncounterDef
{
    public const string EncounterIdEntry = "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER";

    public MysteriousKnightEventEncounter()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("MYSTERIOUS_KNIGHT"))
        };
    }
}
