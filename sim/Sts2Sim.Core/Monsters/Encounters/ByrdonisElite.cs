namespace Sts2Sim.Core.Monsters;

public sealed class ByrdonisElite : EncounterDef
{
    public const string EncounterIdEntry = "BYRDONIS_ELITE";

    public ByrdonisElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("BYRDONIS"))
        };
    }
}
