using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Sts2Sim.Vec;

/// <summary>
/// Raw access to the caller-owned buffers: <c>buffers[k]</c> points at a C-contiguous (N, *shape_k) array, so slot i's
/// row of buffer k starts at <c>base_k + i * BytesPerEnv(k)</c>.
/// </summary>
public static unsafe class BufferView
{
    public static Span<T> Get<T>(void** buffers, int k, int slot) where T : unmanaged
    {
        Debug.Assert(sizeof(T) == Schema.Buffers[k].ElementSize, $"buffer {Schema.Buffers[k].Name} has element size {Schema.Buffers[k].ElementSize}, not {sizeof(T)}");
        byte* p = (byte*)buffers[k] + (nint)slot * Schema.BytesPerEnv(k);
        return new Span<T>(p, Schema.Elements(k));
    }

    public static Span<byte> Bytes(void** buffers, int k, int slot) =>
        new((byte*)buffers[k] + (nint)slot * Schema.BytesPerEnv(k), Schema.BytesPerEnv(k));

    /// <summary>True when every one of the <see cref="Buf.Count"/> pointers is non-null.</summary>
    public static bool AllNonNull(void** buffers)
    {
        if (buffers == null)
        {
            return false;
        }
        for (int k = 0; k < Buf.Count; k++)
        {
            if (buffers[k] == null)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>Typed per-slot views of every buffer (one slot's row of each table).</summary>
public readonly unsafe struct SlotBuffers
{
    private readonly void** _buffers;

    public SlotBuffers(void** buffers, int slot)
    {
        _buffers = buffers;
        Slot = slot;
    }

    public int Slot { get; }

    public Span<T> Get<T>(int k) where T : unmanaged => BufferView.Get<T>(_buffers, k, Slot);

    public Span<byte> Bytes(int k) => BufferView.Bytes(_buffers, k, Slot);

    public void Clear(int k) => Bytes(k).Clear();

    /// <summary>Zero-fills every observation buffer (<see cref="Buf.Observation"/>) of this slot.</summary>
    public void ClearObservation()
    {
        foreach (int k in Buf.Observation)
        {
            Bytes(k).Clear();
        }
    }

    public Span<int> GlobalI => Get<int>(Buf.GlobalI);
    public Span<float> GlobalF => Get<float>(Buf.GlobalF);
    /// <summary>[CAP_HAND * HC], row-major.</summary>
    public Span<int> Hand => Get<int>(Buf.Hand);
    public Span<float> HandF => Get<float>(Buf.HandF);
    public Span<float> HandDmg => Get<float>(Buf.HandDmg);
    public Span<int> Deck => Get<int>(Buf.Deck);
    public Span<short> DrawCounts => Get<short>(Buf.DrawCounts);
    public Span<short> DiscardCounts => Get<short>(Buf.DiscardCounts);
    public Span<short> ExhaustCounts => Get<short>(Buf.ExhaustCounts);
    public Span<int> Relics => Get<int>(Buf.Relics);
    public Span<int> Potions => Get<int>(Buf.Potions);
    public Span<int> PowersPlayer => Get<int>(Buf.PowersPlayer);
    public Span<int> Orbs => Get<int>(Buf.Orbs);
    public Span<int> Enemies => Get<int>(Buf.Enemies);
    public Span<int> EnemyIntents => Get<int>(Buf.EnemyIntents);
    public Span<int> EnemyPowers => Get<int>(Buf.EnemyPowers);
    public Span<int> Allies => Get<int>(Buf.Allies);
    public Span<int> AllyPowers => Get<int>(Buf.AllyPowers);
    public Span<int> MapNodes => Get<int>(Buf.MapNodes);
    public Span<int> Rewards => Get<int>(Buf.Rewards);
    public Span<int> CardReward => Get<int>(Buf.CardReward);
    public Span<int> CardRewardAlts => Get<int>(Buf.CardRewardAlts);
    public Span<int> Shop => Get<int>(Buf.Shop);
    public Span<int> Rest => Get<int>(Buf.Rest);
    public Span<int> EventOptions => Get<int>(Buf.EventOptions);
    public Span<int> PromptOptions => Get<int>(Buf.PromptOptions);
    public Span<int> Cand => Get<int>(Buf.Cand);
    public Span<float> Aux => Get<float>(Buf.Aux);
    public Span<long> Episode => Get<long>(Buf.Episode);
    public Span<int> LastEpisode => Get<int>(Buf.LastEpisode);
    public Span<int> GlobalX => Get<int>(Buf.GlobalX);
    /// <summary>[CAP_HAND * 2], row i = hand row i.</summary>
    public Span<int> HandX => Get<int>(Buf.HandX);
    public Span<int> DrawMarked => Get<int>(Buf.DrawMarked);
    public Span<int> DiscardOrder => Get<int>(Buf.DiscardOrder);
    public Span<int> ExhaustOrder => Get<int>(Buf.ExhaustOrder);
    public Span<int> EventOptionValues => Get<int>(Buf.EventOptionValues);
    /// <summary>[CAP_REWARD_CHOICES * (3 + HC)], row k = rewards-screen choice k (appended 2026-11-04).</summary>
    public Span<int> RewardChoices => Get<int>(Buf.RewardChoices);

    public ref int CandN => ref Get<int>(Buf.CandN)[0];
    public ref float Reward => ref Get<float>(Buf.Reward)[0];
    public ref byte Terminated => ref Get<byte>(Buf.Terminated)[0];
    public ref byte Truncated => ref Get<byte>(Buf.Truncated)[0];
    public ref int Status => ref Get<int>(Buf.Status)[0];
}

/// <summary>Native (unmanaged, zeroed) buffers for N envs in schema order: for C# tests and the bench (Python allocates its own).</summary>
public sealed unsafe class NativeBuffers : IDisposable
{
    private readonly void*[] _ptrs = new void*[Buf.Count];
    private void** _table;

    public NativeBuffers(int numEnvs)
    {
        NumEnvs = numEnvs;
        _table = (void**)NativeMemory.AllocZeroed((nuint)(Buf.Count * sizeof(void*)));
        for (int k = 0; k < Buf.Count; k++)
        {
            _ptrs[k] = NativeMemory.AllocZeroed((nuint)Math.Max(1L, (long)numEnvs * Schema.BytesPerEnv(k)));
            _table[k] = _ptrs[k];
        }
    }

    public int NumEnvs { get; }

    /// <summary>The void** table to pass to <see cref="VecEnv"/> (valid until Dispose).</summary>
    public void** Table => _table;

    /// <summary>All N rows of buffer k.</summary>
    public Span<T> All<T>(int k) where T : unmanaged => new(_ptrs[k], NumEnvs * Schema.Elements(k));

    public Span<byte> AllBytes(int k) => new(_ptrs[k], NumEnvs * Schema.BytesPerEnv(k));

    public SlotBuffers Slot(int slot) => new(_table, slot);

    public void Dispose()
    {
        if (_table == null)
        {
            return;
        }
        for (int k = 0; k < Buf.Count; k++)
        {
            NativeMemory.Free(_ptrs[k]);
            _ptrs[k] = null;
        }
        NativeMemory.Free(_table);
        _table = null;
    }
}
