namespace Sts2Sim.Core.Monsters;

public sealed class MechaKnightElite : EncounterDef
{
    public const string EncounterIdEntry = "MECHA_KNIGHT_ELITE";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "MECHA_KNIGHT" });

    public MechaKnightElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("MECHA_KNIGHT")) };
}
