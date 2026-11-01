namespace Sts2Sim.Core.Monsters;

public sealed class CeremonialBeastBoss : EncounterDef
{
    public const string EncounterIdEntry = "CEREMONIAL_BEAST_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "CEREMONIAL_BEAST" });

    public CeremonialBeastBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("CEREMONIAL_BEAST")) };
}
