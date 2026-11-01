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
        => throw new NotImplementedException("W3.5 kernel-gaps: CombatState.EncounterExtra (GremlinMercNormal.MarkGoldStolen sets GoldWasStolen; Models.Encounters/GremlinMercNormal.cs:51-54)");

    public static decimal CalculateGoldProportion(CombatState cs)
        => throw new NotImplementedException("W3.5 kernel-gaps: CombatState.EscapedCreatures + CombatState.EncounterExtra (GremlinMercNormal.CalculateGoldProportion; Models.Encounters/GremlinMercNormal.cs:56-67)");
}
