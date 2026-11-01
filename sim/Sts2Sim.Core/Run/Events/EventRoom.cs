namespace Sts2Sim.Core.Run;

public sealed class EventRoom : AbstractRoom
{
    public EventRoom(EventModel canonicalEvent)
    {
        ArgumentNullException.ThrowIfNull(canonicalEvent);
        CanonicalEvent = canonicalEvent;
    }

    public EventModel CanonicalEvent { get; }

    public EventModel? LocalMutableEvent { get; private set; }

    public override RoomType RoomType => RoomType.Event;

    public override string? ModelId => CanonicalEvent.Id;

    protected override void EnterInternal(RunState rs, bool isRestoringRoomStackBase)
    {
        BeginEvent(rs);
        var localEvent = LocalMutableEvent!;
        Hook.AfterRoomEntered(rs, this);
        localEvent.AfterEventStarted(rs);
    }

    private void BeginEvent(RunState rs)
    {
        var previous = LocalMutableEvent;
        if (previous != null && !previous.IsFinished)
        {
            previous.EnsureCleanup();
        }
        var player = rs.Players[0];
        var mutable = CanonicalEvent.ToMutable();
        LocalMutableEvent = mutable;
        mutable.BeginEvent(rs, player, IsPreFinished);
        mutable.StateChanged = OnEventStateChanged;
        if (mutable.IsFinished && !IsPreFinished)
        {
            OnEventStateChanged(mutable);
        }
    }

    public override void Exit(RunState rs)
    {
        var localEvent = LocalMutableEvent;
        if (localEvent != null)
        {
            localEvent.StateChanged = null;
            localEvent.EnsureCleanup();
        }
    }

    public override void Resume(RunState rs, AbstractRoom exitedRoom)
    {
        var localEvent = LocalMutableEvent ?? throw new InvalidOperationException("Resumed an event room that was never entered.");
        localEvent.Resume(rs, exitedRoom);
    }

    private void OnEventStateChanged(EventModel eventModel)
    {
        if (eventModel is not AncientEventModel)
        {
            return;
        }
        if (!eventModel.IsFinished)
        {
            return;
        }
        MarkPreFinished();
    }

    public override AbstractRoom CloneFor(RunState rs)
    {
        var clone = new EventRoom(CanonicalEvent)
        {
            Id = Id
        };
        if (IsPreFinished)
        {
            clone.MarkPreFinished();
        }
        var localEvent = LocalMutableEvent;
        if (localEvent != null)
        {
            var owner = localEvent.Owner;
            PlayerState? clonedOwner = null;
            if (owner != null)
            {
                int ownerIndex = IndexOfPlayer(rs, owner);
                clonedOwner = rs.Players[ownerIndex < 0 ? 0 : ownerIndex];
            }
            var clonedEvent = localEvent.CloneFor(clonedOwner, localEvent.Rng?.DeepClone());
            if (localEvent.StateChanged != null)
            {
                clonedEvent.StateChanged = clone.OnEventStateChanged;
            }
            clone.LocalMutableEvent = clonedEvent;
        }
        return clone;
    }

    private static int IndexOfPlayer(RunState rs, PlayerState owner)
    {
        var players = rs.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].CombatId == owner.CombatId)
            {
                return i;
            }
        }
        return -1;
    }

    public override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(EventsCanonicalTags.EventRoom);
        w.WriteInt32(Id);
        w.WriteBool(IsPreFinished);
        w.WriteString(CanonicalEvent.Id);
        var localEvent = LocalMutableEvent;
        w.WriteBool(localEvent != null);
        localEvent?.WriteCanonical(w);
    }
}
