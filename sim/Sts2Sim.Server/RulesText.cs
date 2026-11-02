namespace Sts2Sim.Server;

// Human-readable names and rules text for observations (cards, relics, potions, powers, keywords, enemies, events).
// Scaffold stub: every lookup returns null until Load() discovers and parses the localisation data.
// Owner 'rules-text' (wave E1) implements Load and the lookups; the observation call sites are already wired
// and render nothing extra while a lookup returns null.
public static class RulesText
{
    public static bool Loaded { get; private set; }

    // knowledgeDir: explicit localisation/knowledge directory, or null to auto-discover. No-op in the stub.
    public static void Load(string? knowledgeDir)
    {
        _ = knowledgeDir;
        Loaded = false;
    }

    public static string? CardTitle(string cardId) => null;

    // Description with {Var} placeholders filled from the instance (upgrades, enchantments, live vars).
    public static string? CardText(CardInstance card) => null;

    public static string? RelicTitle(string relicId) => null;

    public static string? RelicText(string relicId) => null;

    public static string? RelicText(RelicModel relic) => RelicText(relic.Id);

    public static string? PotionTitle(string potionId) => null;

    public static string? PotionText(string potionId) => null;

    public static string? PowerText(string powerId) => null;

    public static string? KeywordText(string keyword) => null;

    public static string? EnemyTitle(string monsterId) => null;

    public static string? EnemyMoveTitle(string monsterId, string moveId) => null;

    public static string? EventTitle(string eventId) => null;

    public static string? EventOptionText(string eventId, string optionKey) => null;
}
