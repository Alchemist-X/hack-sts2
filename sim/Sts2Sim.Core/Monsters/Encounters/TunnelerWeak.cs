namespace Sts2Sim.Core.Monsters;

public sealed class TunnelerWeak : EncounterDef
{
    public const string EncounterIdEntry = "TUNNELER_WEAK";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "TUNNELER" });
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Burrower });

    public TunnelerWeak()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override bool IsWeak => true;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("TUNNELER")) };
}
