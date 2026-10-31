namespace Sts2Sim.Core.Monsters;

public sealed class ShrinkerBeetleWeak : EncounterDef
{
    public const string EncounterIdEntry = "SHRINKER_BEETLE_WEAK";

    public ShrinkerBeetleWeak()
        : base(EncounterIdEntry)
    {
    }

    public override bool IsWeak => true;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
    {
        var spawn = new MonsterSpawn(MonsterRegistry.Get(ShrinkerBeetle.MonsterIdEntry));
        return new[] { spawn };
    }
}
