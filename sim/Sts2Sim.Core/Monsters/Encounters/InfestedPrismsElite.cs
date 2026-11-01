namespace Sts2Sim.Core.Monsters;

public sealed class InfestedPrismsElite : EncounterDef
{
    public const string EncounterIdEntry = "INFESTED_PRISMS_ELITE";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "INFESTED_PRISM" });

    public InfestedPrismsElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("INFESTED_PRISM")) };
}
