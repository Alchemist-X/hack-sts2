namespace Sts2Sim.Core.Run;

public sealed class Neow : AncientEventModel
{
    public const string EventId = "NEOW";

    public const string CursedDonePage = "NEOW.pages.DONE.CURSED.description";

    public const string PositiveDonePage = "NEOW.pages.DONE.POSITIVE.description";

    public const string UnportedAllowedAtNeowSite = "Neow: relic def not ported; IsAllowedAtNeow evaluated from Neow's copy of the relic rule";

    public static readonly IReadOnlyList<string> PositiveRelicIds = new[]
    {
        "ARCANE_SCROLL",
        "BOOMING_CONCH",
        "FISHING_ROD",
        "GOLDEN_PEARL",
        "KALEIDOSCOPE",
        "LEAD_PAPERWEIGHT",
        "LOST_COFFER",
        "MASSIVE_SCROLL",
        "NEOWS_TORMENT",
        "NEW_LEAF",
        "PHIAL_HOLSTER",
        "PRECISE_SCISSORS",
        "SCROLL_BOXES",
        "WINGED_BOOTS"
    };

    public static readonly IReadOnlyList<string> CurseRelicIds = new[]
    {
        "CURSED_PEARL",
        "DOWSING_ROD",
        "HEFTY_TABLET",
        "LARGE_CAPSULE",
        "LEAFY_POULTICE",
        "NEOWS_BONES",
        "NEOWS_SACRIFICE",
        "PRECARIOUS_SHEARS",
        "SILKEN_TRESS",
        "SILVER_CRUCIBLE"
    };

    public const string LavaRockId = "LAVA_ROCK";

    public const string NeowsTalismanId = "NEOWS_TALISMAN";

    public const string NutritiousOysterId = "NUTRITIOUS_OYSTER";

    public const string PomanderId = "POMANDER";

    public const string SmallCapsuleId = "SMALL_CAPSULE";

    public const string StoneHumidifierId = "STONE_HUMIDIFIER";

    public override string Id => EventId;

    public override string InitialDescription
    {
        get
        {
            var owner = Owner;
            if (owner != null && ModifierCount <= 0)
            {
                return base.InitialDescription;
            }
            return Id + ".EVENT.description";
        }
    }

    private int ModifierCount { get; set; }

    private EventOption NeowRelicOption(string relicId, string donePage) => AncientRelicOption(relicId, InitialPageKey, donePage);

    private List<EventOption> CurseOptions()
    {
        var list = new List<EventOption>(CurseRelicIds.Count);
        for (int i = 0; i < CurseRelicIds.Count; i++)
        {
            var id = CurseRelicIds[i];
            list.Add(NeowRelicOption(id, id == "NEOWS_BONES" ? PositiveDonePage : CursedDonePage));
        }
        return list;
    }

    private List<EventOption> PositiveOptions()
    {
        var list = new List<EventOption>(PositiveRelicIds.Count);
        for (int i = 0; i < PositiveRelicIds.Count; i++)
        {
            list.Add(NeowRelicOption(PositiveRelicIds[i], PositiveDonePage));
        }
        return list;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Neow has no owner.");
        var rng = Rng ?? throw new InvalidOperationException("Neow has no rng.");
        ModifierCount = rs.Modifiers.Count;
        if (rs.Modifiers.Count > 0)
        {
            throw new NotImplementedException("W2 R5: Neow modifier options (Models.Events/Neow.cs:365-387) — ModifierModel.GenerateNeowOption is not ported");
        }
        var curses = CurseOptions();
        curses.RemoveAll(o => !IsAllowedAtNeow(rs, player, o));
        var curse = rng.NextItem((IEnumerable<EventOption>)curses)!;
        var positives = PositiveOptions();
        switch (curse.RelicId)
        {
            case "CURSED_PEARL":
                positives.RemoveAll(o => o.RelicId == "GOLDEN_PEARL");
                break;
            case "HEFTY_TABLET":
                positives.RemoveAll(o => o.RelicId == "ARCANE_SCROLL");
                break;
            case "LEAFY_POULTICE":
                positives.RemoveAll(o => o.RelicId == "NEW_LEAF");
                break;
            case "PRECARIOUS_SHEARS":
                positives.RemoveAll(o => o.RelicId == "PRECISE_SCISSORS");
                break;
            case "NEOWS_SACRIFICE":
                positives.RemoveAll(o => o.RelicId == "PHIAL_HOLSTER");
                positives.RemoveAll(o => o.RelicId == "LOST_COFFER");
                break;
        }
        if (curse.RelicId != "LARGE_CAPSULE")
        {
            positives.Add(NeowRelicOption(rng.NextBool() ? LavaRockId : SmallCapsuleId, PositiveDonePage));
        }
        positives.Add(NeowRelicOption(rng.NextBool() ? NutritiousOysterId : StoneHumidifierId, PositiveDonePage));
        positives.Add(NeowRelicOption(rng.NextBool() ? NeowsTalismanId : PomanderId, PositiveDonePage));
        positives.RemoveAll(o => !IsAllowedAtNeow(rs, player, o));
        var result = new List<EventOption>(3);
        result.AddRange(new List<EventOption>(positives).UnstableShuffle(rng).Take(2));
        result.Add(curse);
        return result;
    }

    private static bool IsAllowedAtNeow(RunState rs, PlayerState player, EventOption option)
    {
        var relic = option.Relic;
        if (relic != null)
        {
            return relic.IsAllowedAtNeow(rs, player);
        }
        var relicId = option.RelicId;
        if (relicId == null)
        {
            return true;
        }
        rs.Fidelity.Record(FidelityFlag.ApproximatedRule, UnportedAllowedAtNeowSite + " (" + relicId + ")");
        return UnportedIsAllowedAtNeow(rs, relicId);
    }

    private static bool UnportedIsAllowedAtNeow(RunState rs, string relicId)
    {
        switch (relicId)
        {
            case "MASSIVE_SCROLL":
                return rs.Players.Count > 1;
            case "WINGED_BOOTS":
            case "SILVER_CRUCIBLE":
                return rs.Players.Count == 1;
            case "KALEIDOSCOPE":
                return rs.Unlocks.CharacterCardPools.Count == CardPools.AllCharacterIds.Count;
            default:
                return true;
        }
    }

    protected override void WriteAncientFields(CanonicalWriter w)
    {
        w.WriteInt32(ModifierCount);
    }
}
