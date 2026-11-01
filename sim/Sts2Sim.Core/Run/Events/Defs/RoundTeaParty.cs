namespace Sts2Sim.Core.Run;

public sealed class RoundTeaParty : EventModel
{
    public const string EventId = "ROUND_TEA_PARTY";

    public const string RoyalPoisonId = "ROYAL_POISON";

    public const decimal Damage = 11m;

    public const int MinCurrentHp = 12;

    public override string Id => EventId;

    public override bool IsAllowed(RunState runState) => runState.Players.All(p => p.CurrentHp >= MinCurrentHp);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("ROUND_TEA_PARTY.pages.INITIAL.options.ENJOY_TEA", EnjoyTea),
            new EventOption("ROUND_TEA_PARTY.pages.INITIAL.options.PICK_FIGHT", PickFight)
        };
    }

    private void EnjoyTea(RunState rs)
    {
        var target = Owner!;
        Events121Support.ObtainRelic(rs, RoyalPoisonId, target);
        Events121Support.Heal(rs, target, target.MaxHp - target.CurrentHp);
        SetEventFinished("ROUND_TEA_PARTY.pages.ENJOY_TEA.description");
    }

    private void PickFight(RunState rs)
    {
        SetEventState("ROUND_TEA_PARTY.pages.PICK_FIGHT.description", new[]
        {
            new EventOption("ROUND_TEA_PARTY.pages.PICK_FIGHT.options.CONTINUE_FIGHT", ContinueFight).ThatWontSaveToChoiceHistory()
        });
    }

    private void ContinueFight(RunState rs)
    {
        var owner = Owner!;
        Events121Support.Damage(rs, owner, Damage, Events121Support.HpLossProps);
        RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, owner), owner);
        SetEventFinished("ROUND_TEA_PARTY.pages.CONTINUE_FIGHT.description");
    }
}
