namespace Sts2Sim.Core.Monsters;

public sealed class KnowledgeDemonBoss : EncounterDef
{
    public const string EncounterIdEntry = "KNOWLEDGE_DEMON_BOSS";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "KNOWLEDGE_DEMON" });

    public KnowledgeDemonBoss()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Boss;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("KNOWLEDGE_DEMON")) };
}
