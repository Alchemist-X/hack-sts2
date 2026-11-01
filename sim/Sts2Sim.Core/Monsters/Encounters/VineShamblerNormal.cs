namespace Sts2Sim.Core.Monsters;

public sealed class VineShamblerNormal : EncounterDef
{
    public const string EncounterIdEntry = "VINE_SHAMBLER_NORMAL";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "VINE_SHAMBLER" });

    public VineShamblerNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("VINE_SHAMBLER")) };
}
