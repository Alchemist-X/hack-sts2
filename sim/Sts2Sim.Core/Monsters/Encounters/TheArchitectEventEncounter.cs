namespace Sts2Sim.Core.Monsters;

public class TheArchitectEventEncounter : EncounterDef
{
    public const string EncounterIdEntry = "THE_ARCHITECT_EVENT_ENCOUNTER";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "ARCHITECT" });

    public TheArchitectEventEncounter()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("ARCHITECT")) };
}
