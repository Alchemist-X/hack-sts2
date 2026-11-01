namespace Sts2Sim.Core.Monsters;

public sealed class PunchOffEventEncounter : EncounterDef
{
    public const string EncounterIdEntry = "PUNCH_OFF_EVENT_ENCOUNTER";
    public const int MinStartingHpReduction = 2;
    public const int MaxStartingHpReductionExclusive = 10;

    public PunchOffEventEncounter()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var punch = MonsterRegistry.Get("PUNCH_CONSTRUCT");
        int firstReduction = rng.NextInt(MinStartingHpReduction, MaxStartingHpReductionExclusive);
        int secondReduction = rng.NextInt(MinStartingHpReduction, MaxStartingHpReductionExclusive);
        var first = new MonsterSpawn(punch, null, m =>
        {
            EncounterMonsterConfig.SetExtra(m, "StartsWithFastPunch", true);
            EncounterMonsterConfig.SetExtra(m, "StartingHpReduction", firstReduction);
        });
        var second = new MonsterSpawn(punch, null, EncounterMonsterConfig.Set("StartingHpReduction", secondReduction));
        return new[] { first, second };
    }
}
