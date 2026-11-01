namespace Sts2Sim.Core.Run;

public abstract partial class AbstractRoom : ICanonical
{
    public abstract RoomType RoomType { get; }

    public virtual string? ModelId => null;

    public int Id { get; protected set; } = -1;

    public bool IsPreFinished { get; protected set; }

    public virtual bool IsVictoryRoom => false;

    public virtual void Enter(RunState rs) => throw new NotImplementedException("W2 R4: AbstractRoom.Enter");

    public virtual void Exit(RunState rs) => throw new NotImplementedException("W2 R4: AbstractRoom.Exit");

    public virtual void Resume(RunState rs, AbstractRoom exitedRoom) => throw new NotImplementedException("W2 R4: AbstractRoom.Resume");

    public void MarkPreFinished() => IsPreFinished = true;

    public virtual AbstractRoom CloneFor(RunState rs) => throw new NotImplementedException("W2 R4: AbstractRoom.CloneFor");

    public virtual void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R4: AbstractRoom.WriteCanonical");
}
