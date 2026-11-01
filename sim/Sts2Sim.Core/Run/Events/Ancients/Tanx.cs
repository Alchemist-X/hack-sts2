namespace Sts2Sim.Core.Run;

public sealed class Tanx : AncientEventModel
{
    public const string EventId = "TANX";

    public const string TriBoomerangId = "TRI_BOOMERANG";

    public static readonly IReadOnlyList<string> BaseOptionPoolIds = new[]
    {
        "CLAWS",
        "CROSSBOW",
        "IRON_CLUB",
        "MEAT_CLEAVER",
        "SAI",
        "SPIKED_GAUNTLETS",
        "TANXS_WHISTLE",
        "THROWING_AXE",
        "WAR_HAMMER"
    };

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Tanx has no owner.");
        var rng = Rng ?? throw new InvalidOperationException("Tanx has no rng.");
        var pool = new List<EventOption>(BaseOptionPoolIds.Count + 1);
        for (int i = 0; i < BaseOptionPoolIds.Count; i++)
        {
            pool.Add(AncientRelicOption(BaseOptionPoolIds[i]));
        }
        var instinct = EnchantmentRegistry.Create(Instinct.EnchantmentId);
        var deck = player.Deck;
        int count = 0;
        for (int i = 0; i < deck.Count; i++)
        {
            if (instinct.CanEnchant(deck[i]))
            {
                count++;
            }
        }
        if (count >= 3)
        {
            pool.Add(AncientRelicOption(TriBoomerangId));
        }
        return pool.UnstableShuffle(rng).Take(3).ToList();
    }
}
