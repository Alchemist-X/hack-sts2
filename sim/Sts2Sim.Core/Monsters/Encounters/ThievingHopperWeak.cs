namespace Sts2Sim.Core.Monsters;

[Sts2Sim.Core.Registry.SimExclude]
public sealed class ThievingHopperWeak : EncounterDef
{
    public const string EncounterIdEntry = "THIEVING_HOPPER_WEAK";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "THIEVING_HOPPER" });
    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Thieves });

    public ThievingHopperWeak()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override bool IsWeak => true;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("THIEVING_HOPPER")) };
}
