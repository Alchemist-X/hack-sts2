namespace Sts2Sim.Core.Run;

public sealed class MorphicGrove : EventModel
{
    public const string EventId = "MORPHIC_GROVE";

    public const int TransformCount = 2;

    public const int MaxHp = 5;

    public const int MinGold = 100;

    public override string Id => EventId;

    public override bool IsShared => true;

    public override bool IsAllowed(RunState runState)
    {
        var players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].Gold < MinGold || players[i].Deck.Count(c => c.IsTransformable(null)) < 2)
            {
                return false;
            }
        }
        return true;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => new[]
    {
        new EventOption("MORPHIC_GROVE.pages.INITIAL.options.GROUP", Group),
        new EventOption("MORPHIC_GROVE.pages.INITIAL.options.LONER", Loner)
    };

    private void Loner(RunState rs)
    {
        EventKit3456.GainMaxHp(rs, EventKit3456.OwnerOf(this), MaxHp);
        SetEventFinished("MORPHIC_GROVE.pages.LONER.description");
    }

    private void Group(RunState rs)
    {
        var owner = EventKit3456.OwnerOf(this);
        PlayerCmd.LoseGold(rs, owner.Gold, owner, GoldLossType.Stolen);
        var selected = EventKit3456.FromDeckForTransformation(rs, owner, new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, TransformCount), "MorphicGrove.Group", Id).ToList();
        for (int i = 0; i < selected.Count; i++)
        {
            EventKit3456.TransformToRandomInDeck(rs, selected[i], EventKit3456.RngOf(this));
        }
        SetEventFinished("MORPHIC_GROVE.pages.GROUP.description");
    }
}
