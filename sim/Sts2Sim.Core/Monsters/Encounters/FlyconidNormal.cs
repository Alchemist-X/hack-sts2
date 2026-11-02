namespace Sts2Sim.Core.Monsters;

public sealed class FlyconidNormal : EncounterDef
{
    public const string EncounterIdEntry = "FLYCONID_NORMAL";

    private static readonly IReadOnlyList<EncounterTag> TagList = Array.AsReadOnly(new[] { EncounterTag.Mushroom, EncounterTag.Slimes });

    private static readonly IReadOnlyList<string> MediumSlimeIds = Array.AsReadOnly(new[] { LeafSlimeM.MonsterIdEntry, TwigSlimeM.MonsterIdEntry });

    public FlyconidNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<EncounterTag> Tags => TagList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var slime = new MonsterSpawn(MonsterRegistry.Get(rng.NextItem(MediumSlimeIds)!));
        var flyconid = new MonsterSpawn(MonsterRegistry.Get("FLYCONID"));
        return new[] { slime, flyconid };
    }
}
