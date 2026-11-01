namespace Sts2Sim.Core.Run;

public abstract partial class AbstractRoom : ICanonical
{
    public const int NoId = -1;

    public abstract RoomType RoomType { get; }

    public virtual string? ModelId => null;

    public int Id { get; protected set; } = NoId;

    public bool IsPreFinished { get; protected set; }

    public virtual bool IsVictoryRoom => false;

    public virtual void Enter(RunState rs) => Enter(rs, false);

    public void Enter(RunState rs, bool isRestoringRoomStackBase)
    {
        ArgumentNullException.ThrowIfNull(rs);
        Id = rs.NextRoomId++;
        EnterInternal(rs, isRestoringRoomStackBase);
    }

    protected virtual void EnterInternal(RunState rs, bool isRestoringRoomStackBase) => throw new NotImplementedException($"W2 {GetType().Name}.EnterInternal");

    public virtual void Exit(RunState rs) => throw new NotImplementedException($"W2 {GetType().Name}.Exit");

    public virtual void Resume(RunState rs, AbstractRoom exitedRoom) => throw new NotImplementedException($"W2 {GetType().Name}.Resume");

    public void MarkPreFinished() => IsPreFinished = true;

    public virtual AbstractRoom CloneFor(RunState rs) => throw new NotImplementedException($"W2 {GetType().Name}.CloneFor");

    protected T CopyBaseInto<T>(T clone) where T : AbstractRoom
    {
        clone.Id = Id;
        clone.IsPreFinished = IsPreFinished;
        return clone;
    }

    public virtual void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Room);
        w.WriteByte((byte)RoomType);
        w.WriteString(ModelId);
        w.WriteInt32(Id);
        w.WriteBool(IsPreFinished);
        WritePayload(w);
    }

    protected virtual void WritePayload(CanonicalWriter w)
    {
    }

    protected static void RejectRoomStackReconstruction(bool isRestoringRoomStackBase, string roomName)
    {
        if (isRestoringRoomStackBase)
        {
            throw new InvalidOperationException($"{roomName} does not support room stack reconstruction.");
        }
    }
}
