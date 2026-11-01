namespace Sts2Sim.Core.Monsters;

public sealed class FogmogNormal : EncounterDef
{
    public const string EncounterIdEntry = "FOGMOG_NORMAL";
    public const string IllusionSlot = "illusion";
    public const string FogmogSlot = "fogmog";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "FOGMOG", "EYE_WITH_TEETH" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { IllusionSlot, FogmogSlot });

    public FogmogNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("FOGMOG"), FogmogSlot) };
}
