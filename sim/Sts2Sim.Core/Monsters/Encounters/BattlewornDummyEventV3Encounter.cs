namespace Sts2Sim.Core.Monsters;

public sealed class BattlewornDummyEventV3Encounter : BattlewornDummyEventEncounter
{
    public const string EncounterIdEntry = "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER";
    private const string SettingKey = "Setting";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "BATTLE_FRIEND_V3" });

    public BattlewornDummyEventV3Encounter()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override bool ShouldGiveRewards => false;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("BATTLE_FRIEND_V3")) };
}
