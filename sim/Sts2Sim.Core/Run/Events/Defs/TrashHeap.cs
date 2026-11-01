namespace Sts2Sim.Core.Run;

public sealed class TrashHeap : EventModel
{
    public const string EventId = "TRASH_HEAP";

    public const decimal HpLoss = 8m;

    public const int Gold = 100;

    public static readonly IReadOnlyList<string> RelicIds = new[]
    {
        "DARKSTONE_PERIAPT",
        "DREAM_CATCHER",
        "HAND_DRILL",
        "MAW_BANK",
        "THE_BOOT"
    };

    public static readonly IReadOnlyList<string> CardIds = new[]
    {
        "CALTROPS",
        "CLASH",
        "DISTRACTION",
        "DUAL_WIELD",
        "ENTRENCH",
        "HELLO_WORLD",
        "OUTMANEUVER",
        "REBOUND",
        "RIP_AND_TEAR",
        "STACK"
    };

    public override string Id => EventId;

    public override bool IsAllowed(RunState runState) => runState.Players.All(p => p.CurrentHp > 5);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("TRASH_HEAP.pages.INITIAL.options.DIVE_IN", DiveIn),
            new EventOption("TRASH_HEAP.pages.INITIAL.options.GRAB", Grab)
        };
    }

    private void DiveIn(RunState rs)
    {
        Events121Support.Damage(rs, Owner!, (int)HpLoss, Events121Support.HpLossProps);
        string relicId = Rng!.NextItem(RelicIds)!;
        Events121Support.ObtainRelic(rs, relicId, Owner!);
        SetEventFinished("TRASH_HEAP.pages.DIVE_IN.description");
    }

    private void Grab(RunState rs)
    {
        PlayerCmd.GainGold(rs, Gold, Owner!);
        string cardId = Rng!.NextItem(CardIds)!;
        Events121Support.AddCardToDeck(rs, cardId, Owner!);
        SetEventFinished("TRASH_HEAP.pages.GRAB.description");
    }
}
