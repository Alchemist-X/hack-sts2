namespace Sts2Sim.Core.Run;

public sealed class Tezcatara : AncientEventModel
{
    public const string EventId = "TEZCATARA";

    public const string NutritiousSoupId = "NUTRITIOUS_SOUP";

    public static readonly IReadOnlyList<string> OptionPool1Ids = new[] { "VERY_HOT_COCOA", "YUMMY_COOKIE" };

    public static readonly IReadOnlyList<string> OptionPool2Ids = new[] { "BIIIG_HUG", "STORYBOOK", "TOASTY_MITTENS" };

    public static readonly IReadOnlyList<string> OptionPool3Ids = new[] { "GOLDEN_COMPASS", "PUMPKIN_CANDLE", "TOY_BOX", "SEAL_OF_GOLD" };

    public override string Id => EventId;

    private List<EventOption> Pool(IReadOnlyList<string> ids)
    {
        var list = new List<EventOption>(ids.Count + 1);
        for (int i = 0; i < ids.Count; i++)
        {
            list.Add(AncientRelicOption(ids[i]));
        }
        return list;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Tezcatara has no owner.");
        var rng = Rng ?? throw new InvalidOperationException("Tezcatara has no rng.");
        var pool1 = Pool(OptionPool1Ids);
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].HasTag(CardTag.Strike) && deck[i].Rarity == CardRarity.Basic)
            {
                pool1.Add(AncientRelicOption(NutritiousSoupId));
                break;
            }
        }
        var first = rng.NextItem((IEnumerable<EventOption>)pool1)!;
        var second = rng.NextItem((IEnumerable<EventOption>)Pool(OptionPool2Ids))!;
        var third = rng.NextItem((IEnumerable<EventOption>)Pool(OptionPool3Ids))!;
        return new[] { first, second, third };
    }
}
