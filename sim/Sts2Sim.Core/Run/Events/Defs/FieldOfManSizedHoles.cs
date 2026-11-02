namespace Sts2Sim.Core.Run;

public sealed class FieldOfManSizedHoles : EventModel
{
    public const string EventId = "FIELD_OF_MAN_SIZED_HOLES";

    public const int Gold = 75;

    public const int Cards = 2;

    public const string NormalityId = "NORMALITY";

    public const string PerfectFitId = "PERFECT_FIT";

    public override string Id => EventId;

    // Option-text vars (Models.Events/FieldOfManSizedHoles.cs:225 StringVar ResistCurse / Enchantment).
    public static string ResistCurse => NormalityId;

    public static string Enchantment => PerfectFitId;

    public override bool IsAllowed(RunState runState)
    {
        var perfectFit = EnchantmentRegistry.Create(PerfectFitId);
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (!players[i].Deck.Any(perfectFit.CanEnchant))
            {
                return false;
            }
        }
        return true;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.RESIST", Resist),
        new EventOption("FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.ENTER_YOUR_HOLE", EnterYourHole)
    };

    private void Resist(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var cards = EventKit3456.FromDeckForRemoval(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, Cards), "FieldOfManSizedHoles.Resist", Id);
        CardPileCmd.RemoveFromDeck(rs, cards);
        CardPileCmd.AddCursesToDeck(rs, new[] { CardRegistry.Get(NormalityId) }, owner);
        SetEventFinished("FIELD_OF_MAN_SIZED_HOLES.pages.RESIST.description");
    }

    private void EnterYourHole(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var selected = EventKit3456.FromDeckForEnchantment(rs, owner, PerfectFitId, null, new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1), "FieldOfManSizedHoles.EnterYourHole", Id);
        if (selected.Count > 0)
        {
            CardCmd.Enchant(PerfectFitId, selected[0], 1m);
        }
        SetEventFinished("FIELD_OF_MAN_SIZED_HOLES.pages.ENTER_YOUR_HOLE.description");
    }
}
