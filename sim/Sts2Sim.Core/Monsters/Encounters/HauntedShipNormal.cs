namespace Sts2Sim.Core.Monsters;

public sealed class HauntedShipNormal : EncounterDef
{
    public const string EncounterIdEntry = "HAUNTED_SHIP_NORMAL";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "HAUNTED_SHIP" });

    public HauntedShipNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("HAUNTED_SHIP")) };
}
