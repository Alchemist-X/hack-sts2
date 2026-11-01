namespace Sts2Sim.Core.Monsters;

public sealed class GremlinMercNormal : EncounterDef
{
    public const string EncounterIdEntry = "GREMLIN_MERC_NORMAL";
    public const string MercSlot = "merc";
    public const string SneakySlot = "sneaky";
    public const string FatSlot = "fat";
    public const string GremlinMercId = "GREMLIN_MERC";
    public const string FatGremlinId = "FAT_GREMLIN";
    public const string SneakyGremlinId = "SNEAKY_GREMLIN";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { GremlinMercId, FatGremlinId, SneakyGremlinId });

    public GremlinMercNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get(GremlinMercId), MercSlot) };

    public override EncounterExtraState CreateExtraState() => new Extra();

    public static bool GoldWasStolen(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        return cs.GetEncounterExtra<Extra>()?.GoldWasStolen ?? false;
    }

    public static void MarkGoldStolen(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var extra = cs.GetEncounterExtra<Extra>()
            ?? throw new InvalidOperationException($"GremlinMercNormal.MarkGoldStolen: combat {cs.EncounterId} carries no GREMLIN_MERC_NORMAL encounter state.");
        extra.GoldWasStolen = true;
    }

    public override float CalculateGoldProportion(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        bool fatEscaped = false;
        var escaped = cs.EscapedCreatures;
        for (int i = 0; i < escaped.Count; i++)
        {
            if (escaped[i] is MonsterState monster && monster.Def.Id == FatGremlinId)
            {
                fatEscaped = true;
                break;
            }
        }
        if (!fatEscaped)
        {
            return 1f;
        }
        if (!GoldWasStolen(cs))
        {
            return 0.5f;
        }
        return 0f;
    }

    public sealed class Extra : EncounterExtraState
    {
        public bool GoldWasStolen { get; set; }

        public override EncounterExtraState DeepClone() => new Extra { GoldWasStolen = GoldWasStolen };

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(MonsterCanonicalTags.GremlinMercNormalEncounterExtra);
            w.WriteBool(GoldWasStolen);
        }
    }
}
