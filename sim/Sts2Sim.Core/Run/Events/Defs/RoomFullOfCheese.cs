namespace Sts2Sim.Core.Run;

public sealed class RoomFullOfCheese : EventModel
{
    public const string EventId = "ROOM_FULL_OF_CHEESE";

    public const int Damage = 14;

    public const int GorgeCardCount = 8;

    public const int GorgePickCount = 2;

    public const string ChosenCheeseId = "CHOSEN_CHEESE";

    public override string Id => EventId;

    public override bool IsAllowed(RunState runState) => runState.CurrentActIndex < 2;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("ROOM_FULL_OF_CHEESE.pages.INITIAL.options.GORGE", Gorge),
        new EventOption("ROOM_FULL_OF_CHEESE.pages.INITIAL.options.SEARCH", Search)
    };

    private void Gorge(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var options = CardCreationOptions.ForNonCombatWithUniformOdds(new[] { CardPools.ForCharacter(owner.CharacterId) }, c => c.Rarity == CardRarity.Common).WithFlags(CardCreationFlags.NoRarityModification);
        var cards = CardFactory.CreateForReward(rs, owner, GorgeCardCount, options).ToList();
        var prefs = new CardSelectorPrefs("ROOM_FULL_OF_CHEESE.pages.GORGE.selectionScreenPrompt", GorgePickCount);
        EventKit3456.SelectCardsToAddToDeckFromGrid(rs, owner, cards, prefs, "RoomFullOfCheese.Gorge", Id);
        SetEventFinished("ROOM_FULL_OF_CHEESE.pages.GORGE.description");
    }

    private void Search(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        EventKit3456.Damage(rs, owner, Damage, ValueProp.Unblockable | ValueProp.Unpowered);
        EventKit3456.ObtainRelic(rs, ChosenCheeseId, owner);
        SetEventFinished("ROOM_FULL_OF_CHEESE.pages.SEARCH.description");
    }
}
