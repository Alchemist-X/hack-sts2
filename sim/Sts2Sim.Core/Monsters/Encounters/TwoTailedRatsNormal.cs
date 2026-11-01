namespace Sts2Sim.Core.Monsters;

public sealed class TwoTailedRatsNormal : EncounterDef
{
    public const string EncounterIdEntry = "TWO_TAILED_RATS_NORMAL";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "TWO_TAILED_RAT" });
    private static readonly IReadOnlyList<string> SlotNames = Array.AsReadOnly(new[] { "first", "second", "third", "fourth", "fifth" });

    public TwoTailedRatsNormal()
        : base(EncounterIdEntry)
    {
    }

    public override IReadOnlyList<string> Slots => SlotNames;

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var rat = MonsterRegistry.Get("TWO_TAILED_RAT");
        int num = rng.NextInt(3);
        int second = (num + 1) % 3;
        int third = (num + 2) % 3;
        return new[]
        {
            new MonsterSpawn(rat, Slots[2], m => EncounterMonsterConfig.Set(m, "StarterMoveIndex", num)),
            new MonsterSpawn(rat, Slots[3], m => EncounterMonsterConfig.Set(m, "StarterMoveIndex", second)),
            new MonsterSpawn(rat, Slots[4], m => EncounterMonsterConfig.Set(m, "StarterMoveIndex", third))
        };
    }
}
