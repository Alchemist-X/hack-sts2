namespace Sts2Sim.Core.Monsters;

public sealed class LivingFogNormal : EncounterDef
{
    public const string EncounterIdEntry = "LIVING_FOG_NORMAL";
    private const string LivingFogSlot = "livingFog";
    private const string BombSlotPrefix = "bomb";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "LIVING_FOG", "GAS_BOMB" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { "bomb1", "bomb2", "bomb3", "bomb4", "bomb5", LivingFogSlot });

    public LivingFogNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("LIVING_FOG"), LivingFogSlot) };
}
