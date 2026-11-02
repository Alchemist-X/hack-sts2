namespace Sts2Sim.Core.Monsters;

public sealed class SoulNexusElite : EncounterDef
{
    public const string EncounterIdEntry = "SOUL_NEXUS_ELITE";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "SOUL_NEXUS" });

    public SoulNexusElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("SOUL_NEXUS")) };
}
