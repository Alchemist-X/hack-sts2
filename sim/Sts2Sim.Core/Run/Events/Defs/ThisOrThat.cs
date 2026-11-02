namespace Sts2Sim.Core.Run;

public sealed class ThisOrThat : EventModel
{
    public const string EventId = "THIS_OR_THAT";

    public const string ClumsyId = "CLUMSY";

    public const decimal HpLoss = 6m;

    private decimal _gold;

    public override string Id => EventId;

    public decimal Gold => _gold;

    public override void CalculateVars(RunState rs)
    {
        _gold = Rng!.NextInt(41, 69);
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("THIS_OR_THAT.pages.INITIAL.options.PLAIN", Plain),
            new EventOption("THIS_OR_THAT.pages.INITIAL.options.ORNATE", Ornate)
        };
    }

    private void Plain(RunState rs)
    {
        Events121Support.Damage(rs, Owner!, (int)HpLoss, Events121Support.HpLossProps);
        PlayerCmd.GainGold(rs, (int)_gold, Owner!);
        SetEventFinished("THIS_OR_THAT.pages.PLAIN.description");
    }

    private void Ornate(RunState rs)
    {
        var owner = Owner!;
        RelicCmd.Obtain(rs, RelicFactory.PullNextRelicFromFront(rs, owner), owner);
        Events121Support.AddCurseToDeck(rs, ClumsyId, owner);
        SetEventFinished("THIS_OR_THAT.pages.ORNATE.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteDecimal(_gold);
    }
}
