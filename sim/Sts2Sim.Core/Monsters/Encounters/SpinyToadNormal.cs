namespace Sts2Sim.Core.Monsters;

public sealed class SpinyToadNormal : EncounterDef
{
    public const string EncounterIdEntry = "SPINY_TOAD_NORMAL";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "SPINY_TOAD" });

    public SpinyToadNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("SPINY_TOAD")) };
}
