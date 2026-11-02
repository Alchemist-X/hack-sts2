namespace Sts2Sim.Core.Monsters;

public sealed class HunterKillerNormal : EncounterDef
{
    public const string EncounterIdEntry = "HUNTER_KILLER_NORMAL";

    public HunterKillerNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("HUNTER_KILLER"))
        };
    }
}
