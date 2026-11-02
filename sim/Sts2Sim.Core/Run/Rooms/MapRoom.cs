namespace Sts2Sim.Core.Run;

public sealed class MapRoom : AbstractRoom
{
    public override RoomType RoomType => RoomType.Map;

    protected override void EnterInternal(RunState rs, bool isRestoringRoomStackBase)
    {
        RejectRoomStackReconstruction(isRestoringRoomStackBase, nameof(MapRoom));
    }

    public override void Exit(RunState rs)
    {
    }

    public override void Resume(RunState rs, AbstractRoom exitedRoom) => throw new InvalidOperationException("MapRoom cannot be resumed.");

    public override AbstractRoom CloneFor(RunState rs) => CopyBaseInto(new MapRoom());
}
