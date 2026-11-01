namespace Sts2Sim.Core.Monsters;

public sealed class SlimedBerserkerNormal : EncounterDef
{
    public const string EncounterIdEntry = "SLIMED_BERSERKER_NORMAL";

    public SlimedBerserkerNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        return new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("SLIMED_BERSERKER"))
        };
    }
}
