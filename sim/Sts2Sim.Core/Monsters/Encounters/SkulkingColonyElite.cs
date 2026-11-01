namespace Sts2Sim.Core.Monsters;

public sealed class SkulkingColonyElite : EncounterDef
{
    public const string EncounterIdEntry = "SKULKING_COLONY_ELITE";

    public SkulkingColonyElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("SKULKING_COLONY"))
        };
    }
}
