namespace Sts2Sim.Core.Monsters;

public sealed class TestSubjectBoss : EncounterDef
{
    public const string EncounterIdEntry = "TEST_SUBJECT_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "TEST_SUBJECT" });

    public TestSubjectBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("TEST_SUBJECT")) };
}
