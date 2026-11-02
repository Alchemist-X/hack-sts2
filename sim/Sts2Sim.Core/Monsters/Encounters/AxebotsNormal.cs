namespace Sts2Sim.Core.Monsters;

public sealed class AxebotsNormal : EncounterDef
{
    public const string EncounterIdEntry = "AXEBOTS_NORMAL";
    private const string FrontSlot = "front";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "AXEBOT" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { FrontSlot });

    public AxebotsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> Slots => SlotNames;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("AXEBOT"), FrontSlot) };
}
