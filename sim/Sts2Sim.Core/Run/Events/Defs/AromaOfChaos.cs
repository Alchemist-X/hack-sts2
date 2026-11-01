namespace Sts2Sim.Core.Run;

public sealed class AromaOfChaos : EventModel
{
    public const string EventId = "AROMA_OF_CHAOS";

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("AROMA_OF_CHAOS.pages.INITIAL.options.LET_GO", LetGo),
        new EventOption("AROMA_OF_CHAOS.pages.INITIAL.options.MAINTAIN_CONTROL", MaintainControl)
    };

    private void LetGo(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var selected = EventKit3456.FromDeckForTransformation(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1), "AromaOfChaos.LetGo", Id);
        if (selected.Count > 0)
        {
            EventKit3456.TransformToRandomInDeck(rs, selected[0], EventKit3456.RngOf(this));
        }
        SetEventFinished("AROMA_OF_CHAOS.pages.LET_GO.description");
    }

    private void MaintainControl(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        var selected = EventKit3456.FromDeckForUpgrade(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, 1), "AromaOfChaos.MaintainControl", Id);
        if (selected.Count > 0)
        {
            CardCmd.Upgrade(null, selected[0]);
        }
        SetEventFinished("AROMA_OF_CHAOS.pages.MAINTAIN_CONTROL.description");
    }
}
