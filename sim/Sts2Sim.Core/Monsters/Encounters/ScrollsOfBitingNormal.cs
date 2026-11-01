namespace Sts2Sim.Core.Monsters;

public sealed class ScrollsOfBitingNormal : EncounterDef
{
    public const string EncounterIdEntry = "SCROLLS_OF_BITING_NORMAL";
    public const int StarterMoveCount = 3;
    public const int FourthScrollStarterMoveIdx = 2;

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Scrolls });

    public ScrollsOfBitingNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var scroll = MonsterRegistry.Get("SCROLL_OF_BITING");
        int num = rng.NextInt(StarterMoveCount);
        return new[]
        {
            new MonsterSpawn(scroll, null, EncounterMonsterConfig.Set("StarterMoveIdx", num)),
            new MonsterSpawn(scroll, null, EncounterMonsterConfig.Set("StarterMoveIdx", (num + 1) % StarterMoveCount)),
            new MonsterSpawn(scroll, null, EncounterMonsterConfig.Set("StarterMoveIdx", (num + 2) % StarterMoveCount)),
            new MonsterSpawn(scroll, null, EncounterMonsterConfig.Set("StarterMoveIdx", FourthScrollStarterMoveIdx))
        };
    }
}
