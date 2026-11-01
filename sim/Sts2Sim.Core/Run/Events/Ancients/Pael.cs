namespace Sts2Sim.Core.Run;

public sealed class Pael : AncientEventModel
{
    public const string EventId = "PAEL";

    public const string PaelsClawId = "PAELS_CLAW";

    public const string PaelsToothId = "PAELS_TOOTH";

    public const string PaelsGrowthId = "PAELS_GROWTH";

    public const string PaelsLegionId = "PAELS_LEGION";

    public const string PaelsWingId = "PAELS_WING";

    public const string ByrdonisEggCardId = "BYRDONIS_EGG";

    public static readonly IReadOnlyList<string> OptionPool1Ids = new[] { "PAELS_FLESH", "PAELS_HORN", "PAELS_TEARS" };

    public static readonly IReadOnlyList<string> OptionPool3Ids = new[] { "PAELS_EYE", "PAELS_BLOOD" };

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var player = Owner ?? throw new InvalidOperationException("Pael has no owner.");
        var rng = Rng ?? throw new InvalidOperationException("Pael has no rng.");
        var pool1 = new List<EventOption>(OptionPool1Ids.Count);
        for (int i = 0; i < OptionPool1Ids.Count; i++)
        {
            pool1.Add(AncientRelicOption(OptionPool1Ids[i]));
        }
        var first = rng.NextItem((IEnumerable<EventOption>)pool1)!;
        var pool2 = new List<EventOption> { AncientRelicOption(PaelsWingId) };
        var deck = player.Deck;
        var goopy = EnchantmentRegistry.Create(Goopy.EnchantmentId);
        int goopyCount = 0;
        int removableCount = 0;
        for (int i = 0; i < deck.Count; i++)
        {
            if (goopy.CanEnchant(deck[i]))
            {
                goopyCount++;
            }
            if (deck[i].IsRemovable)
            {
                removableCount++;
            }
        }
        if (goopyCount >= 3)
        {
            pool2.Add(AncientRelicOption(PaelsClawId));
        }
        if (removableCount >= 5)
        {
            pool2.Add(AncientRelicOption(PaelsToothId));
        }
        pool2.AddRange(pool2);
        pool2.Add(AncientRelicOption(PaelsGrowthId));
        var second = rng.NextItem((IEnumerable<EventOption>)pool2)!;
        var pool3 = new List<EventOption>(OptionPool3Ids.Count + 1);
        for (int i = 0; i < OptionPool3Ids.Count; i++)
        {
            pool3.Add(AncientRelicOption(OptionPool3Ids[i]));
        }
        if (!HasEventPet(player))
        {
            pool3.Add(AncientRelicOption(PaelsLegionId));
        }
        var third = rng.NextItem((IEnumerable<EventOption>)pool3)!;
        return new[] { first, second, third };
    }

    private static bool HasEventPet(PlayerState player)
    {
        var relics = player.Relics;
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i].AddsPet)
            {
                return true;
            }
        }
        var deck = player.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck[i].Id == ByrdonisEggCardId)
            {
                return true;
            }
        }
        return false;
    }
}
