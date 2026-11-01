namespace Sts2Sim.Core.Monsters;

public sealed class AeonglassBoss : EncounterDef
{
    public const string EncounterIdEntry = "AEONGLASS_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "AEONGLASS" });

    public AeonglassBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("AEONGLASS")) };
}
