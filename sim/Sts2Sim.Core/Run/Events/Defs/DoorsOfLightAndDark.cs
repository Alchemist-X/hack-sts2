namespace Sts2Sim.Core.Run;

public sealed class DoorsOfLightAndDark : EventModel
{
    public const string EventId = "DOORS_OF_LIGHT_AND_DARK";

    public const int Cards = 2;

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("DOORS_OF_LIGHT_AND_DARK.pages.INITIAL.options.LIGHT", Light),
        new EventOption("DOORS_OF_LIGHT_AND_DARK.pages.INITIAL.options.DARK", Dark)
    };

    private void Light(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var upgradable = owner.Deck.Where(c => c.IsUpgradable).ToList();
        var picked = upgradable.StableShuffle(EventKit3456.RngOf(this), CardPileCmd.CompareForShuffle).Take(Cards).ToList();
        for (int i = 0; i < picked.Count; i++)
        {
            CardCmd.Upgrade(null, picked[i]);
        }
        SetEventFinished("DOORS_OF_LIGHT_AND_DARK.pages.LIGHT.description");
    }

    private void Dark(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var cards = EventKit3456.FromDeckForRemoval(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1), "DoorsOfLightAndDark.Dark", Id);
        CardPileCmd.RemoveFromDeck(rs, cards);
        SetEventFinished("DOORS_OF_LIGHT_AND_DARK.pages.DARK.description");
    }
}
