namespace Sts2Sim.Core.Run;

[SimExclude]
public sealed class SimStubEvent : EventModel
{
    public const string ProceedKey = "PROCEED";

    public const string StubSite = "SimStubEvent: unported event replaced by a single PROCEED option";

    private readonly string _id;

    public SimStubEvent(string replacedEventId)
    {
        ArgumentException.ThrowIfNullOrEmpty(replacedEventId);
        _id = replacedEventId;
    }

    public override string Id => _id;

    public override EventModel ToMutable() => new SimStubEvent(_id);

    public static EventModel CreateOrStub(string eventId)
        => EventRegistry.Models.Contains(eventId) ? EventRegistry.Create(eventId) : new SimStubEvent(eventId);

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        rs.Fidelity.Record(FidelityFlag.ApproximatedRule, StubSite + " (" + _id + ")");
        return new[] { new EventOption(ProceedKey, Proceed, disableOnChosen: false, isProceed: true) };
    }

    private void Proceed(RunState rs)
    {
        SetEventFinished("PROCEED.description");
    }
}
