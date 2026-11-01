namespace Sts2Sim.Core.Monsters;

public sealed class RubyRaidersNormal : EncounterDef
{
    public const string EncounterIdEntry = "RUBY_RAIDERS_NORMAL";
    public const int RaiderCount = 3;

    private static readonly string[] RaiderIds = { "AXE_RUBY_RAIDER", "ASSASSIN_RUBY_RAIDER", "BRUTE_RUBY_RAIDER", "CROSSBOW_RUBY_RAIDER", "TRACKER_RUBY_RAIDER" };

    private static readonly int[] RaiderValidCounts = { 1, 1, 1, 1, 1 };

    public RubyRaidersNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var currentRaiders = new List<int>();
        var spawns = new List<MonsterSpawn>(RaiderCount);
        for (int i = 0; i < RaiderCount; i++)
        {
            var items = EncounterPools.ValidIndices(RaiderValidCounts, currentRaiders);
            int picked = rng.NextItem(items);
            currentRaiders.Add(picked);
            spawns.Add(new MonsterSpawn(MonsterRegistry.Get(RaiderIds[picked])));
        }
        return spawns;
    }
}
