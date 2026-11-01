namespace Sts2Sim.Core.Run;

public sealed class TheLanternKey : EventModel
{
    public const string EventId = "THE_LANTERN_KEY";

    public const string LanternKeyId = "LANTERN_KEY";

    public const decimal Gold = 100m;

    public override string Id => EventId;

    public override EventLayoutType LayoutType => EventLayoutType.Combat;

    public override EncounterDef? CanonicalEncounter => EncounterRegistry.Get(MysteriousKnightEventEncounter.EncounterIdEntry);

    public override bool IsShared => true;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption(InitialOptionKey("RETURN_THE_KEY"), ReturnTheKey),
        new EventOption(InitialOptionKey("KEEP_THE_KEY"), KeepTheKey)
    };

    private void ReturnTheKey(RunState rs)
    {
        PlayerCmd.GainGold(rs, Gold, Owner!);
        SetEventFinished(Id + ".pages.DONE.options.RETURN_THE_KEY.description");
    }

    private void KeepTheKey(RunState rs)
    {
        SetEventState(Id + ".pages.KEEP_THE_KEY.description", new[]
        {
            new EventOption(OptionKey("KEEP_THE_KEY", "FIGHT"), Fight)
        });
    }

    private void Fight(RunState rs)
    {
        var owner = Owner!;
        var extraRewards = new List<Reward>
        {
            new SpecialCardReward(CardFactory.CreateRunCard(rs, RequireCard(LanternKeyId), owner), owner)
        };
        EnterCombatWithoutExitingEvent(rs, EncounterRegistry.Get(MysteriousKnightEventEncounter.EncounterIdEntry), extraRewards, shouldResumeAfterCombat: false);
    }

    private static CardDef RequireCard(string cardId)
        => CardRegistry.TryGet(cardId, out var def) ? def : throw new NotImplementedException("W3 cross-owner: card " + cardId + " is not ported");
}
