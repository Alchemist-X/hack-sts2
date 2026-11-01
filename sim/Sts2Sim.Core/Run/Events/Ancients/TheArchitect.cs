namespace Sts2Sim.Core.Run;

public sealed class TheArchitect : EventModel
{
    public const string EventId = "THE_ARCHITECT";

    public const string EmptyDescription = "PROCEED.description";

    public const string DialogueDroppedSite = "TheArchitect: dialogue pages (Models.Events/TheArchitect.cs:1042-1064) collapsed into the single PROCEED option";

    public override string Id => EventId;

    public override EventLayoutType LayoutType => EventLayoutType.Combat;

    public bool RunWon { get; private set; }

    protected override void SetInitialEventState(RunState rs, bool isPreFinished)
    {
        SetEventState(EmptyDescription, GenerateInitialOptionsWrapper(rs));
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[] { new EventOption("PROCEED", WinRun, disableOnChosen: false).ThatWontSaveToChoiceHistory() };
    }

    private void WinRun(RunState rs)
    {
        RunWon = true;
        SetEventState(EmptyDescription, Array.Empty<EventOption>());
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteBool(RunWon);
    }
}
