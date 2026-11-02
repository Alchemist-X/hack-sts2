namespace Sts2Sim.Core.State;

public interface IOwnedListener<in TOwner> where TOwner : class
{
    AbstractModel CloneFor(TOwner newOwner);
}

public static class ListenerCanonical
{
    public const byte None = 0;

    public const byte Stateless = 1;

    public const byte Stateful = 2;

    public static void Write(CanonicalWriter w, AbstractModel? listener)
    {
        ArgumentNullException.ThrowIfNull(w);
        if (listener is null)
        {
            w.WriteByte(None);
            return;
        }
        if (listener is ICanonical canonical)
        {
            w.WriteByte(Stateful);
            canonical.WriteCanonical(w);
            return;
        }
        w.WriteByte(Stateless);
    }
}
