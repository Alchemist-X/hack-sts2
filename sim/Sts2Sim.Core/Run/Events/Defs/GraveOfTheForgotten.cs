namespace Sts2Sim.Core.Run;

public sealed class GraveOfTheForgotten : EventModel
{
    public const string EventId = "GRAVE_OF_THE_FORGOTTEN";

    public const string ForgottenSoulId = "FORGOTTEN_SOUL";

    public const string SoulsPowerId = "SOULS_POWER";

    public const string DecayId = "DECAY";

    public override string Id => EventId;

    // Option-text var (Models.Events/GraveOfTheForgotten.cs:201 StringVar Enchantment = SoulsPower's title).
    public static string Enchantment => SoulsPowerId;

    public override bool IsAllowed(RunState runState)
    {
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (!HasEnchantableCards(players[i]))
            {
                return false;
            }
        }
        return true;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var confront = HasEnchantableCards(EventKit3456.OwnerOf(this))
            ? new EventOption("GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT", Confront)
            : new EventOption("GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT_LOCKED", null);
        return new[]
        {
            confront,
            new EventOption("GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.ACCEPT", Accept)
        };
    }

    private void Confront(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        CardPileCmd.AddCurseToDeck(rs, DecayId, owner);
        var selected = EventKit3456.FromDeckForEnchantment(rs, owner, SoulsPowerId, null, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), "GraveOfTheForgotten.Confront", Id);
        if (selected.Count > 0)
        {
            CardCmd.Enchant(SoulsPowerId, selected[0], 1m);
        }
        SetEventFinished("GRAVE_OF_THE_FORGOTTEN.pages.CONFRONT.description");
    }

    private void Accept(RunState rs)
    {
        EventKit3456.ObtainRelic(rs, ForgottenSoulId, EventKit3456.OwnerOf(this));
        SetEventFinished("GRAVE_OF_THE_FORGOTTEN.pages.ACCEPT.description");
    }

    private static bool HasEnchantableCards(PlayerState player)
    {
        var soulsPower = EnchantmentRegistry.Create(SoulsPowerId);
        return player.Deck.Any(soulsPower.CanEnchant);
    }
}
