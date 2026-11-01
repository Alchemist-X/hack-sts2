namespace Sts2Sim.Core.Run;

public sealed class UnrestSite : EventModel
{
    public const string EventId = "UNREST_SITE";

    public const string PoorSleepId = "POOR_SLEEP";

    public const decimal MaxHpLoss = 8m;

    public const decimal MaxCurrentHpFraction = 0.70m;

    private decimal _heal;

    public override string Id => EventId;

    public decimal Heal => _heal;

    public override bool IsAllowed(RunState runState) => runState.Players.All(p => p.CurrentHp <= p.MaxHp * MaxCurrentHpFraction);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("UNREST_SITE.pages.INITIAL.options.REST", Rest),
            new EventOption("UNREST_SITE.pages.INITIAL.options.KILL", Kill)
        };
    }

    public override void CalculateVars(RunState rs)
    {
        _heal = Owner!.MaxHp - Owner.CurrentHp;
    }

    private void Rest(RunState rs)
    {
        Events121Support.Heal(rs, Owner!, _heal);
        Events121Support.AddCurseToDeck(rs, PoorSleepId, Owner!);
        SetEventFinished("UNREST_SITE.pages.REST.description");
    }

    private void Kill(RunState rs)
    {
        var owner = Owner!;
        Events121Support.LoseMaxHp(rs, owner, MaxHpLoss, isFromCard: false);
        RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, owner), owner);
        SetEventFinished("UNREST_SITE.pages.KILL.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteDecimal(_heal);
    }
}
