namespace Sts2Sim.Core.Run;

public abstract partial class EventModel : AbstractModel, ICanonical
{
    public abstract string Id { get; }

    public virtual bool IsShared => false;

    public PlayerState? Owner { get; protected set; }

    public Rng? Rng { get; protected set; }

    public IReadOnlyList<EventOption> CurrentOptions => throw new NotImplementedException("W2 R5: EventModel.CurrentOptions");

    public bool IsFinished => throw new NotImplementedException("W2 R5: EventModel.IsFinished");

    public virtual bool IsAllowed(RunState runState) => true;

    public void BeginEvent(RunState rs, PlayerState player, bool isPreFinished) => throw new NotImplementedException("W2 R5: EventModel.BeginEvent");

    protected virtual IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs) => throw new NotImplementedException("W2 R5: EventModel.GenerateInitialOptions");

    public void ChooseOption(RunState rs, int optionIndex) => throw new NotImplementedException("W2 R5: EventModel.ChooseOption");

    protected void SetEventState(IReadOnlyList<EventOption> options) => throw new NotImplementedException("W2 R5: EventModel.SetEventState");

    protected void EnterCombatWithoutExitingEvent(RunState rs, EncounterDef encounter, List<Reward> extraRewards, bool shouldResumeAfterCombat) => throw new NotImplementedException("W2 R5: EventModel.EnterCombatWithoutExitingEvent");

    public virtual void Resume(RunState rs, AbstractRoom exitedRoom) => throw new NotImplementedException("W2 R5: EventModel.Resume");

    public virtual EventModel CloneFor(PlayerState? owner, Rng? rng) => throw new NotImplementedException("W2 R5: EventModel.CloneFor");

    public virtual void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R5: EventModel.WriteCanonical");
}
