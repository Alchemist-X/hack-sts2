namespace Sts2Sim.Core.Run;

public sealed class SelfHelpBook : EventModel
{
    public const string EventId = "SELF_HELP_BOOK";

    public const string SharpId = "SHARP";

    public const string NimbleId = "NIMBLE";

    public const string SwiftId = "SWIFT";

    public const int SharpAmount = 2;

    public const int NimbleAmount = 2;

    public const int SwiftAmount = 2;

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var options = new List<EventOption>();
        bool sharp = PlayerHasCardsAvailable(owner, SharpId, CardType.Attack);
        bool nimble = PlayerHasCardsAvailable(owner, NimbleId, CardType.Skill);
        bool swift = PlayerHasCardsAvailable(owner, SwiftId, CardType.Power);
        if (sharp || nimble || swift)
        {
            options.Add(sharp
                ? new EventOption("SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK", ReadTheBack)
                : new EventOption("SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK_LOCKED", null));
            options.Add(nimble
                ? new EventOption("SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE", ReadPassage)
                : new EventOption("SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE_LOCKED", null));
            options.Add(swift
                ? new EventOption("SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK", ReadEntireBook)
                : new EventOption("SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK_LOCKED", null));
        }
        else
        {
            options.Add(new EventOption("SELF_HELP_BOOK.pages.INITIAL.options.NO_OPTIONS", SkipBook));
        }
        return options;
    }

    private void ReadTheBack(RunState rs) => SelectAndEnchant(rs, SharpId, SharpAmount, CardType.Attack, "SELF_HELP_BOOK.pages.READ_THE_BACK.description", "SelfHelpBook.ReadTheBack");

    private void ReadPassage(RunState rs) => SelectAndEnchant(rs, NimbleId, NimbleAmount, CardType.Skill, "SELF_HELP_BOOK.pages.READ_PASSAGE.description", "SelfHelpBook.ReadPassage");

    private void ReadEntireBook(RunState rs) => SelectAndEnchant(rs, SwiftId, SwiftAmount, CardType.Power, "SELF_HELP_BOOK.pages.READ_ENTIRE_BOOK.description", "SelfHelpBook.ReadEntireBook");

    private void SelectAndEnchant(RunState rs, string enchantmentId, int amount, CardType typeRestriction, string finalDescription, string siteId)
    {
        var owner = EventKit3456.OwnerOf(this);
        var selected = EventKit3456.FromDeckForEnchantment(rs, owner, enchantmentId, c => c.Type == typeRestriction, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), siteId, Id);
        if (selected.Count > 0)
        {
            CardCmd.Enchant(enchantmentId, selected[0], amount);
        }
        SetEventFinished(finalDescription);
    }

    private static bool PlayerHasCardsAvailable(PlayerState player, string enchantmentId, CardType typeRestriction)
    {
        var enchantment = EnchantmentRegistry.Create(enchantmentId);
        return player.Deck.Any(c => c.Type == typeRestriction && enchantment.CanEnchant(c));
    }

    private void SkipBook(RunState rs)
    {
        SetEventFinished("SELF_HELP_BOOK.pages.NO_OPTIONS.description");
    }
}
