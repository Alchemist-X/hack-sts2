namespace Sts2Sim.Core.Monsters;

public sealed class NibbitsNormal : EncounterDef
{
    public const string EncounterIdEntry = "NIBBITS_NORMAL";
    public const string BackSlot = "back";
    public const string FrontSlot = "front";

    private static readonly IReadOnlyList<string> SlotList = Array.AsReadOnly(new[] { FrontSlot, BackSlot });

    public NibbitsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override IReadOnlyList<string> Slots => SlotList;

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var nibbit = MonsterRegistry.Get(Nibbit.MonsterIdEntry);
        return new[]
        {
            new MonsterSpawn(nibbit, FrontSlot, static m => Nibbit.GetExtra(m).IsFront = true),
            new MonsterSpawn(nibbit, BackSlot)
        };
    }
}
