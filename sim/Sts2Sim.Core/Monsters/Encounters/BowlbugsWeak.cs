namespace Sts2Sim.Core.Monsters;

public sealed class BowlbugsWeak : EncounterDef
{
    public const string EncounterIdEntry = "BOWLBUGS_WEAK";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Workers });

    private static readonly IReadOnlyList<string> BugIds = Array.AsReadOnly(new[] { "BOWLBUG_EGG", "BOWLBUG_NECTAR" });

    public BowlbugsWeak()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var rock = new MonsterSpawn(MonsterRegistry.Get("BOWLBUG_ROCK"), "odd");
        var bug = new MonsterSpawn(MonsterRegistry.Get(rng.NextItem(BugIds)!), "even");
        return new[] { rock, bug };
    }
}
