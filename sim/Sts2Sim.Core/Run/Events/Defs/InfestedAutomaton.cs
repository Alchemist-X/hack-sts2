namespace Sts2Sim.Core.Run;

public sealed class InfestedAutomaton : EventModel
{
    public const string EventId = "INFESTED_AUTOMATON";

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("INFESTED_AUTOMATON.pages.INITIAL.options.STUDY", Study),
            new EventOption("INFESTED_AUTOMATON.pages.INITIAL.options.TOUCH_CORE", TouchCore)
        };
    }

    private void Study(RunState rs)
    {
        var owner = Owner!;
        var options = CardCreationOptions.ForNonCombatWithDefaultOdds(new[] { CardPools.ForCharacter(owner.CharacterId) }, static c => c.Type == CardType.Power);
        AddFirst(rs, owner, options);
        SetEventFinished("INFESTED_AUTOMATON.pages.STUDY.description");
    }

    private void TouchCore(RunState rs)
    {
        var owner = Owner!;
        var options = CardCreationOptions.ForNonCombatWithDefaultOdds(new[] { CardPools.ForCharacter(owner.CharacterId) }, static c => c.CanonicalCost == 0 && !c.CostsX)
            .WithFlags(CardCreationFlags.NoCardPoolModifications);
        AddFirst(rs, owner, options);
        SetEventFinished("INFESTED_AUTOMATON.pages.TOUCH_CORE.description");
    }

    private static void AddFirst(RunState rs, PlayerState owner, CardCreationOptions options)
    {
        var card = CardFactory.CreateForReward(rs, owner, 1, options).FirstOrDefault()?.Card;
        if (card != null)
        {
            CardPileCmd.Add(rs, card, PileType.Deck);
        }
    }
}
