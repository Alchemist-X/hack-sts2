namespace Sts2Sim.Core.Relics;

public sealed class ArchaicTooth : RelicModel, IEventRelicSetup
{
    public const string RelicId = "ARCHAIC_TOOTH";

    public const string StarterCardKey = "StarterCard";

    public const string AncientCardKey = "AncientCard";

    public const string DoubtCardId = "DOUBT";

    public const string NoStarterSite = "ArchaicTooth.AfterObtained: deck has no transcendence starter (the game dereferences null here)";

    private static readonly (string Starter, string Ancient)[] TranscendenceUpgrades =
    {
        ("BASH", "BREAK"),
        ("NEUTRALIZE", "SUPPRESS"),
        ("UNLEASH", "PROTECTOR"),
        ("FALLING_STAR", "METEOR_SHOWER"),
        ("DUALCAST", "QUADCAST"),
    };

    public static IReadOnlyList<string> TranscendenceCards { get; } = TranscendenceUpgrades.Select(u => u.Ancient).ToArray();

    private string? _starterCardId;
    private string? _ancientCardId;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public string? StarterCard => _starterCardId;

    public string? AncientCard => _ancientCardId;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StringVar(StarterCardKey),
        new StringVar(AncientCardKey)
    };

    public bool SetupForPlayer(RunState rs, PlayerState player)
    {
        var starter = GetTranscendenceStarterCard(player);
        if (starter != null)
        {
            _starterCardId = starter.Id;
            _ancientCardId = AncientIdFor(starter.Id) ?? DoubtCardId;
            ((StringVar)DynamicVars[StarterCardKey]).StringValue = _starterCardId;
            ((StringVar)DynamicVars[AncientCardKey]).StringValue = _ancientCardId;
            return true;
        }
        return false;
    }

    public override void AfterObtained(RunState rs)
    {
        var starter = GetTranscendenceStarterCard(Owner);
        if (starter == null)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, NoStarterSite);
            return;
        }
        var replacement = GetTranscendenceTransformedCard(rs, starter);
        CardCmd.Transform(rs, new[] { new CardTransformation(rs.Combat, starter, replacement) }, null);
    }

    private static string? AncientIdFor(string starterId)
    {
        for (int i = 0; i < TranscendenceUpgrades.Length; i++)
        {
            if (string.Equals(TranscendenceUpgrades[i].Starter, starterId, StringComparison.Ordinal))
            {
                return TranscendenceUpgrades[i].Ancient;
            }
        }
        return null;
    }

    private static CardInstance? GetTranscendenceStarterCard(PlayerState player)
        => player.Deck.FirstOrDefault(c => AncientIdFor(c.Id) != null);

    private static CardInstance GetTranscendenceTransformedCard(RunState rs, CardInstance starterCard)
    {
        var ancientId = AncientIdFor(starterCard.Id);
        if (ancientId != null)
        {
            var card = CardFactory.CreateRunCard(rs, CardRegistry.Get(ancientId), starterCard.Owner);
            if (starterCard.IsUpgraded)
            {
                CardCmd.Upgrade(rs.Combat, card);
            }
            if (starterCard.Enchantment != null)
            {
                var enchantment = starterCard.Enchantment.MutableClone();
                CardCmd.Enchant(enchantment, card, enchantment.Amount);
            }
            return card;
        }
        return CardFactory.CreateRunCard(rs, CardRegistry.Get(DoubtCardId), starterCard.Owner);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteString(_starterCardId ?? string.Empty);
        w.WriteString(_ancientCardId ?? string.Empty);
    }
}
