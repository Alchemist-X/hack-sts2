namespace Sts2Sim.Core.Monsters;

public sealed class BattlewornDummyEventV2Encounter : BattlewornDummyEventEncounter
{
    public const string EncounterIdEntry = "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "BATTLE_FRIEND_V2" });

    public BattlewornDummyEventV2Encounter()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override bool ShouldGiveRewards => false;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("BATTLE_FRIEND_V2")) };
}
