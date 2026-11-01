namespace Sts2Sim.Core.Monsters;

public sealed class OvicopterNormal : EncounterDef
{
    public const string EncounterIdEntry = "OVICOPTER_NORMAL";
    private const string OvicopterSlot = "ovicopter";
    private const string EggSlotPrefix = "egg";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "OVICOPTER", "TOUGH_EGG" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { "egg1", "egg2", "egg3", "egg4", "egg5", OvicopterSlot });

    public OvicopterNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("OVICOPTER"), OvicopterSlot) };
}
