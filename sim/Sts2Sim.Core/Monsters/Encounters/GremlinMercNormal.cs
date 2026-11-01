namespace Sts2Sim.Core.Monsters;

[Sts2Sim.Core.Registry.SimExclude]
public sealed class GremlinMercNormal : EncounterDef
{
    public const string EncounterIdEntry = "GREMLIN_MERC_NORMAL";
    public const string MercSlot = "merc";
    public const string SneakySlot = "sneaky";
    public const string FatSlot = "fat";

    private static readonly IReadOnlyList<string> PossibleMonsterIds = Array.AsReadOnly(new[] { "GREMLIN_MERC", "FAT_GREMLIN", "SNEAKY_GREMLIN" });

    public GremlinMercNormal()
        : base(EncounterIdEntry)
    {
    }

    public override RoomType RoomType => RoomType.Monster;

    public override IReadOnlyList<string> AllPossibleMonsters => PossibleMonsterIds;

    public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        => new[] { new MonsterSpawn(MonsterRegistry.Get("GREMLIN_MERC"), MercSlot) };

    public static void MarkGoldStolen(CombatState cs)
        => throw new NotImplementedException("W3 gap: mutable encounter instance state (GremlinMercNormal.GoldWasStolen; EncounterDef is shared and immutable)");

    public static decimal CalculateGoldProportion(CombatState cs)
        => throw new NotImplementedException("W3 gap: CombatState.EscapedCreatures + GoldWasStolen (GremlinMercNormal.CalculateGoldProportion)");
}
