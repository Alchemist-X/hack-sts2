namespace Sts2Sim.Core.Monsters;

public sealed class VantomBoss : EncounterDef
{
    public const string EncounterIdEntry = "VANTOM_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "VANTOM" });

    public VantomBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("VANTOM")) };
}
