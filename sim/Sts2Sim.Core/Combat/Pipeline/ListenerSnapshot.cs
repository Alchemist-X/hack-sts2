using System.Buffers;

namespace Sts2Sim.Core.Combat.Pipeline;

internal enum ListenerSource : byte
{
    Guarded,
    Combat,
    Run
}

internal readonly ref struct ListenerSnapshot
{
    private readonly AbstractModel[]? _items;

    private ListenerSnapshot(AbstractModel[] items, int count)
    {
        _items = items;
        Count = count;
    }

    public int Count { get; }

    public bool TryGetLive(CombatState cs, int index, out AbstractModel model)
    {
        model = _items![index];
        return IsLive(cs, model);
    }

    public static bool IsLive(CombatState cs, AbstractModel model)
    {
        if (model is PowerModel power)
        {
            if (!power.HasOwner)
            {
                return false;
            }
            var owner = power.Owner;
            if (!CombatStatus.IsAttached(cs, owner))
            {
                return false;
            }
            if (owner is PlayerState player)
            {
                return CombatStatus.IsActiveForHooks(cs, player);
            }
            return true;
        }
        return true;
    }

    public static ListenerSnapshot Take(CombatState cs, ListenerSource source)
    {
        if (source == ListenerSource.Guarded && CombatStatus.IsOverOrEnding(cs) && !cs.IsStarting)
        {
            return default;
        }
        var player = cs.Player;
        var monsters = cs.Monsters;
        int n = player.Powers.Count;
        for (int i = 0; i < monsters.Count; i++)
        {
            n += monsters[i].Powers.Count;
        }
        if (n == 0)
        {
            return default;
        }
        var items = ArrayPool<AbstractModel>.Shared.Rent(n);
        int k = 0;
        var playerPowers = player.Powers;
        for (int j = 0; j < playerPowers.Count; j++)
        {
            items[k++] = playerPowers[j];
        }
        for (int i = 0; i < monsters.Count; i++)
        {
            var powers = monsters[i].Powers;
            for (int j = 0; j < powers.Count; j++)
            {
                items[k++] = powers[j];
            }
        }
        return new ListenerSnapshot(items, k);
    }

    public static bool Contains(CombatState cs, ListenerSource source, AbstractModel model)
    {
        using var listeners = Take(cs, source);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var m) && ReferenceEquals(m, model))
            {
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        if (_items != null)
        {
            ArrayPool<AbstractModel>.Shared.Return(_items, clearArray: true);
        }
    }
}
