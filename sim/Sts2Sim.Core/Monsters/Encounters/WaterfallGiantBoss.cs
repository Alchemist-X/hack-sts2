namespace Sts2Sim.Core.Monsters;

public sealed class WaterfallGiantBoss : EncounterDef
{
    public const string EncounterIdEntry = "WATERFALL_GIANT_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "WATERFALL_GIANT" });

    public WaterfallGiantBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("WATERFALL_GIANT")) };
}
