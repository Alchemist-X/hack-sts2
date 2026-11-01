namespace Sts2Sim.Core.Monsters;

public sealed class TheInsatiableBoss : EncounterDef
{
    public const string EncounterIdEntry = "THE_INSATIABLE_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "THE_INSATIABLE" });

    public TheInsatiableBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("THE_INSATIABLE")) };
}
