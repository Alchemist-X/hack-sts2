namespace Sts2Sim.Core.Monsters;

public sealed class TheLostAndForgottenNormal : EncounterDef
{
    public const string EncounterIdEntry = "THE_LOST_AND_FORGOTTEN_NORMAL";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "THE_LOST", "THE_FORGOTTEN" });

    public TheLostAndForgottenNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[]
        {
            new MonsterSpawn(MonsterRegistry.Get("THE_LOST")),
            new MonsterSpawn(MonsterRegistry.Get("THE_FORGOTTEN"))
        };
}
