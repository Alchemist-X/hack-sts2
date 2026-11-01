namespace Sts2Sim.Core.Monsters;

public sealed class SlimesNormal : EncounterDef
{
    public const string EncounterIdEntry = "SLIMES_NORMAL";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Slimes });

    public SlimesNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        bool flag = rng.NextBool();
        string first = flag ? LeafSlimeS.MonsterIdEntry : TwigSlimeS.MonsterIdEntry;
        string second = flag ? TwigSlimeS.MonsterIdEntry : LeafSlimeS.MonsterIdEntry;
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get(TwigSlimeM.MonsterIdEntry)),
            new MonsterSpawn(MonsterRegistry.Get(LeafSlimeM.MonsterIdEntry)),
            new MonsterSpawn(MonsterRegistry.Get(first)),
            new MonsterSpawn(MonsterRegistry.Get(second))
        };
    }
}
