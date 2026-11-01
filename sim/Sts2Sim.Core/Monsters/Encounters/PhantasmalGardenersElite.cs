namespace Sts2Sim.Core.Monsters;

public sealed class PhantasmalGardenersElite : EncounterDef
{
    public const string EncounterIdEntry = "PHANTASMAL_GARDENERS_ELITE";

    private static readonly IReadOnlyList<string> SlotList = Array.AsReadOnly(new[] { "first", "second", "third", "fourth" });

    public PhantasmalGardenersElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<string> Slots => SlotList;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("PHANTASMAL_GARDENER"), "first"),
            new MonsterSpawn(MonsterRegistry.Get("PHANTASMAL_GARDENER"), "second"),
            new MonsterSpawn(MonsterRegistry.Get("PHANTASMAL_GARDENER"), "third"),
            new MonsterSpawn(MonsterRegistry.Get("PHANTASMAL_GARDENER"), "fourth")
        };
    }
}
