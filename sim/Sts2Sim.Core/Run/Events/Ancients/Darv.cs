namespace Sts2Sim.Core.Run;

public sealed class Darv : AncientEventModel
{
    public const string EventId = "DARV";

    public const string DustyTomeId = "DUSTY_TOME";

    private enum SetFilter : byte
    {
        Always,
        NoDeckClearingModifier,
        Act2Only,
        Act2OrLater
    }

    private static readonly (SetFilter Filter, string[] RelicIds)[] ValidRelicSets =
    {
        (SetFilter.Always, new[] { "ASTROLABE" }),
        (SetFilter.Always, new[] { "BLACK_STAR" }),
        (SetFilter.Always, new[] { "CALLING_BELL" }),
        (SetFilter.Always, new[] { "EMPTY_CAGE" }),
        (SetFilter.NoDeckClearingModifier, new[] { "PANDORAS_BOX" }),
        (SetFilter.Always, new[] { "RUNIC_PYRAMID" }),
        (SetFilter.Always, new[] { "SNECKO_EYE" }),
        (SetFilter.Act2Only, new[] { "ECTOPLASM" }),
        (SetFilter.Act2Only, new[] { "SOZU" }),
        (SetFilter.Act2OrLater, new[] { "PHILOSOPHERS_STONE" }),
        (SetFilter.Act2OrLater, new[] { "VELVET_CHOKER" })
    };

    public override string Id => EventId;

    public static IReadOnlyList<string> AllSetRelicIds => ValidRelicSets.SelectMany(s => s.RelicIds).ToArray();

    private static bool Passes(RunState rs, SetFilter filter)
    {
        switch (filter)
        {
            case SetFilter.NoDeckClearingModifier:
                var modifiers = rs.Modifiers;
                for (int i = 0; i < modifiers.Count; i++)
                {
                    if (modifiers[i].ClearsPlayerDeck)
                    {
                        return false;
                    }
                }
                return true;
            case SetFilter.Act2Only:
                return rs.CurrentActIndex == 1;
            case SetFilter.Act2OrLater:
                return rs.CurrentActIndex >= 1;
            default:
                return true;
        }
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var player = Owner;
        var rng = Rng ?? throw new InvalidOperationException("Darv has no rng.");
        var options = new List<EventOption>(ValidRelicSets.Length);
        for (int i = 0; i < ValidRelicSets.Length; i++)
        {
            var set = ValidRelicSets[i];
            if (Passes(rs, set.Filter))
            {
                options.Add(AncientRelicOption(rng.NextItem((IReadOnlyList<string>)set.RelicIds)!));
            }
        }
        options.UnstableShuffle(rng);
        List<EventOption> result;
        if (rng.NextBool())
        {
            result = options.Take(2).ToList();
            var tome = CreateOptionRelic(DustyTomeId);
            if (player != null)
            {
                SetupRelicForPlayer(rs, player, tome, DustyTomeId);
            }
            result.Add(AncientRelicOption(DustyTomeId, tome));
        }
        else
        {
            result = options.Take(3).ToList();
        }
        return result;
    }
}
