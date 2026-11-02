namespace Sts2Sim.Core.Run;

public sealed class Bugslayer : EventModel
{
    public const string EventId = "BUGSLAYER";

    public const string ExterminateId = "EXTERMINATE";

    public const string SquashId = "SQUASH";

    public override string Id => EventId;

    // Option-text vars (Models.Events/Bugslayer.cs:213 StringVar Card1 / Card2).
    public static string Card1 => ExterminateId;

    public static string Card2 => SquashId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            new EventOption("BUGSLAYER.pages.INITIAL.options.EXTERMINATION", Extermination),
            new EventOption("BUGSLAYER.pages.INITIAL.options.SQUASH", Squash)
        };
    }

    private void Extermination(RunState rs) => AddAndPreview(rs, ExterminateId, "BUGSLAYER.pages.EXTERMINATION.description");

    private void Squash(RunState rs) => AddAndPreview(rs, SquashId, "BUGSLAYER.pages.SQUASH.description");

    private void AddAndPreview(RunState rs, string cardId, string description)
    {
        Events121Support.AddCardToDeck(rs, cardId, Owner!);
        SetEventFinished(description);
    }
}
