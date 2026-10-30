namespace Sts2Sim.Core.State;

public static class StateHasher
{
    public static ulong Hash(ReadOnlySpan<byte> canonicalBytes) => XxHash64(canonicalBytes, 0UL);

    public static ulong Hash(ICanonical state, CanonicalWriter? scratch = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var writer = scratch ?? new CanonicalWriter();
        writer.Reset();
        state.WriteCanonical(writer);
        return Hash(writer.WrittenSpan);
    }

    public static byte[] CanonicalBytes(ICanonical state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var writer = new CanonicalWriter();
        state.WriteCanonical(writer);
        return writer.ToArray();
    }

    public static ulong XxHash64(ReadOnlySpan<byte> data, ulong seed) => DeterministicHash.XxHash64(data, unchecked((long)seed));
}
