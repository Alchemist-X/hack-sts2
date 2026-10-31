namespace Sts2Sim.Core.Monsters;

public sealed class SlimesWeak : EncounterDef
{
    public const string EncounterIdEntry = "SLIMES_WEAK";

    private static readonly IReadOnlyList<string> SmallSlimeIds = Array.AsReadOnly(new[] { LeafSlimeS.MonsterIdEntry, TwigSlimeS.MonsterIdEntry });
    private static readonly IReadOnlyList<string> MediumSlimeIds = Array.AsReadOnly(new[] { LeafSlimeM.MonsterIdEntry, TwigSlimeM.MonsterIdEntry });

    public SlimesWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    public static IReadOnlyList<string> SmallSlimes => SmallSlimeIds;

    public static IReadOnlyList<string> MediumSlimes => MediumSlimeIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var small = new List<MonsterDef>(SmallSlimeIds.Count);
        for (int i = 0; i < SmallSlimeIds.Count; i++)
        {
            small.Add(MonsterRegistry.Get(SmallSlimeIds[i]));
        }
        var medium = new MonsterDef[MediumSlimeIds.Count];
        for (int i = 0; i < MediumSlimeIds.Count; i++)
        {
            medium[i] = MonsterRegistry.Get(MediumSlimeIds[i]);
        }
        var first = rng.NextItem(small)!;
        small.Remove(first);
        var second = rng.NextItem(small)!;
        var middle = rng.NextItem(medium)!;
        return new[]
        {
            new MonsterSpawn(first),
            new MonsterSpawn(middle),
            new MonsterSpawn(second)
        };
    }
}
