namespace Sts2Sim.Core.Monsters;

public sealed class FabricatorNormal : EncounterDef
{
    public const string EncounterIdEntry = "FABRICATOR_NORMAL";
    private const string FabricatorSlot = "fabricator";
    private const string BotSlotPrefix = "bot";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "FABRICATOR", "GUARDBOT", "NOISEBOT", "ZAPBOT", "STABBOT" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { "bot1", "bot2", FabricatorSlot, "bot3", "bot4" });

    public FabricatorNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("FABRICATOR"), FabricatorSlot) };
}
