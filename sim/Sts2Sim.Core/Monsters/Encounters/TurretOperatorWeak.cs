namespace Sts2Sim.Core.Monsters;

public sealed class TurretOperatorWeak : EncounterDef
{
    public const string EncounterIdEntry = "TURRET_OPERATOR_WEAK";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "LIVING_SHIELD", "TURRET_OPERATOR" });

    public TurretOperatorWeak()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override bool IsWeak => true;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("LIVING_SHIELD")),
            new MonsterSpawn(MonsterRegistry.Get("TURRET_OPERATOR"))
        };
}
