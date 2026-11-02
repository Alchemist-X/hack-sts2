namespace Sts2Sim.Core.Run;

public sealed class Nonupeipe : AncientEventModel
{
    public const string EventId = "NONUPEIPE";

    public const string BeautifulBraceletId = "BEAUTIFUL_BRACELET";

    public static readonly IReadOnlyList<string> OptionPoolIds = new[]
    {
        "BLESSED_ANTLER",
        "BRILLIANT_SCARF",
        "DELICATE_FROND",
        "DIAMOND_DIADEM",
        "FUR_COAT",
        "GLITTER",
        "JEWELRY_BOX",
        "LOOMING_FRUIT",
        "SIGNET_RING"
    };

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Nonupeipe has no owner.");
        var rng = Rng ?? throw new InvalidOperationException("Nonupeipe has no rng.");
        var pool = new List<EventOption>(OptionPoolIds.Count + 1);
        for (int i = 0; i < OptionPoolIds.Count; i++)
        {
            pool.Add(AncientRelicOption(OptionPoolIds[i]));
        }
        var swift = EnchantmentRegistry.Create(Swift.EnchantmentId);
        var deck = player.Deck;
        int count = 0;
        for (int i = 0; i < deck.Count; i++)
        {
            if (swift.CanEnchant(deck[i]))
            {
                count++;
            }
        }
        if (count >= 4)
        {
            pool.Add(AncientRelicOption(BeautifulBraceletId));
        }
        pool.UnstableShuffle(rng);
        return pool.Take(3).ToList();
    }
}
