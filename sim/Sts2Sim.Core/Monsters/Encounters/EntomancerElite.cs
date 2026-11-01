namespace Sts2Sim.Core.Monsters;

public sealed class EntomancerElite : EncounterDef
{
    public const string EncounterIdEntry = "ENTOMANCER_ELITE";

    public EntomancerElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("ENTOMANCER"))
        };
    }
}
