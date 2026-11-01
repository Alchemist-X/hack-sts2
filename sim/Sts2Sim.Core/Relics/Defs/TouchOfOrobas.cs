namespace Sts2Sim.Core.Relics;

public sealed class TouchOfOrobas : RelicModel, IEventRelicSetup
{
    public const string RelicId = "TOUCH_OF_OROBAS";

    public const string CircletRelicId = "CIRCLET";

    public const string NoStarterSite = "TouchOfOrobas.AfterObtained: obtained without setup and without a starter relic (the game throws; Orobas never offers it then)";

    private const string StarterRelicKey = "StarterRelic";

    private const string UpgradedRelicKey = "UpgradedRelic";

    private static readonly (string Starter, string Upgraded)[] RefinementUpgrades =
    {
        ("BURNING_BLOOD", "BLACK_BLOOD"),
        ("RING_OF_THE_SNAKE", "RING_OF_THE_DRAKE"),
        ("DIVINE_RIGHT", "DIVINE_DESTINY"),
        ("BOUND_PHYLACTERY", "PHYLACTERY_UNBOUND"),
        ("CRACKED_CORE", "INFUSED_CORE")
    };

    private string? _starterRelic;
    private string? _upgradedRelic;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new StringVar(StarterRelicKey),
        new StringVar(UpgradedRelicKey)
    };

    public string? StarterRelic
    {
        get => _starterRelic;
        set
        {
            if (_starterRelic != null)
            {
                throw new InvalidOperationException("Recursive Core setup called twice!");
            }
            _starterRelic = value;
            if (_starterRelic != null)
            {
                ((StringVar)DynamicVars[StarterRelicKey]).StringValue = _starterRelic;
            }
        }
    }

    public string? UpgradedRelic
    {
        get => _upgradedRelic;
        set
        {
            if (_upgradedRelic != null)
            {
                throw new InvalidOperationException("Recursive Core setup called twice!");
            }
            _upgradedRelic = value;
            if (_upgradedRelic != null)
            {
                ((StringVar)DynamicVars[UpgradedRelicKey]).StringValue = _upgradedRelic;
            }
        }
    }

    private static RelicModel? GetStarterRelic(PlayerState p)
        => p.Relics.FirstOrDefault(r => r.Rarity == RelicRarity.Starter);

    public static string GetUpgradedStarterRelicId(RelicModel starterRelic)
    {
        foreach (var (starter, upgraded) in RefinementUpgrades)
        {
            if (string.Equals(starter, starterRelic.Id, StringComparison.Ordinal))
            {
                return upgraded;
            }
        }
        return CircletRelicId;
    }

    public bool SetupForPlayer(RunState rs, PlayerState player)
    {
        var starterRelic = GetStarterRelic(player);
        if (starterRelic != null)
        {
            StarterRelic = starterRelic.Id;
            UpgradedRelic = GetUpgradedStarterRelicId(starterRelic);
            return true;
        }
        return false;
    }

    public void SetupForTests(string starterRelic, string upgradedRelic)
    {
        StarterRelic = starterRelic;
        UpgradedRelic = upgradedRelic;
    }

    public override void AfterObtained(RunState rs)
    {
        if (StarterRelic == null && GetStarterRelic(Owner) == null)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, NoStarterSite);
            return;
        }
        string id = StarterRelic ?? Owner.Relics.First(r => r.Rarity == RelicRarity.Starter).Id;
        var relicById = Owner.Relics.First(r => string.Equals(r.Id, id, StringComparison.Ordinal));
        string id2 = UpgradedRelic ?? GetUpgradedStarterRelicId(relicById);
        var replace = RelicRegistry.Create(id2);
        RelicCmd.Replace(rs, relicById, replace);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_starterRelic != null);
        w.WriteString(_starterRelic ?? string.Empty);
        w.WriteBool(_upgradedRelic != null);
        w.WriteString(_upgradedRelic ?? string.Empty);
    }
}
