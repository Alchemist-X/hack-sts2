namespace Sts2Sim.Core.Monsters;

public sealed class TheObscuraNormal : EncounterDef
{
    public const string EncounterIdEntry = "THE_OBSCURA_NORMAL";
    public const string IllusionSlot = "illusion";
    private const string ObscuraSlot = "obscura";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "THE_OBSCURA", "PARAFRIGHT" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { IllusionSlot, ObscuraSlot });

    public TheObscuraNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("THE_OBSCURA"), ObscuraSlot) };
}
