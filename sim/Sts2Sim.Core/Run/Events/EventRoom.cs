namespace Sts2Sim.Core.Run;

public sealed class EventRoom : AbstractRoom
{
    public EventRoom(EventModel canonicalEvent)
    {
        CanonicalEvent = canonicalEvent;
    }

    public EventModel CanonicalEvent { get; }

    public EventModel? LocalMutableEvent { get; private set; }

    public override RoomType RoomType => RoomType.Event;

    public override string? ModelId => CanonicalEvent.Id;
}
