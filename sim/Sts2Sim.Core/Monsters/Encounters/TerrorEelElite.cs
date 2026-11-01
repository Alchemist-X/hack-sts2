namespace Sts2Sim.Core.Monsters;

public sealed class TerrorEelElite : EncounterDef
{
    public const string EncounterIdEntry = "TERROR_EEL_ELITE";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "TERROR_EEL" });

    public TerrorEelElite()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Elite;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("TERROR_EEL")) };
}
