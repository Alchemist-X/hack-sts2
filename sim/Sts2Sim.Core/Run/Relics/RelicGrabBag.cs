namespace Sts2Sim.Core.Run;

public sealed class RelicGrabBag : ICanonical
{
    private static readonly RelicRarity[] Rarities = { RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop };

    private static readonly Func<string, bool> AcceptAll = static _ => true;

    private readonly List<RelicRarity> _dequeOrder = new();

    private readonly Dictionary<RelicRarity, List<string>> _deques = new();

    private readonly List<string> _mpFallbackDequeue = new();

    private List<string>? _originalRelics;

    public RelicGrabBag() : this(false)
    {
    }

    public RelicGrabBag(bool refreshAllowed)
    {
        RefreshAllowed = refreshAllowed;
    }

    public bool RefreshAllowed { get; }

    public bool IsPopulated => _deques.Count > 0;

    public IReadOnlyList<RelicRarity> DequeOrder => _dequeOrder;

    public IReadOnlyList<string> GetDequeContents(RelicRarity rarity) => _deques.TryGetValue(rarity, out var list) ? list : Array.Empty<string>();

    public static IReadOnlyList<string> SharedPoolRelics(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        return FilterContent(rs, RelicCatalog.GetUnlockedRelics(RelicPools.SharedRelicPool, rs.Unlocks));
    }

    public static RelicGrabBag FromSerializable(IEnumerable<KeyValuePair<RelicRarity, IReadOnlyList<string>>> relicIdLists)
    {
        ArgumentNullException.ThrowIfNull(relicIdLists);
        var bag = new RelicGrabBag();
        bag.LoadFromSerializable(relicIdLists);
        return bag;
    }

    public void LoadFromSerializable(IEnumerable<KeyValuePair<RelicRarity, IReadOnlyList<string>>> relicIdLists)
    {
        foreach (var (rarity, ids) in relicIdLists)
        {
            var deque = GetOrAddDeque(rarity);
            deque.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                if (RelicCatalog.Contains(ids[i]))
                {
                    deque.Add(ids[i]);
                }
            }
        }
    }

    public bool HasAvailableRelics(RunState runState)
    {
        for (int i = 0; i < Rarities.Length; i++)
        {
            if (GetAvailableDeque(Rarities[i], runState, AcceptAll) != null)
            {
                return true;
            }
        }
        return _mpFallbackDequeue.Any(r => RelicCatalog.IsAllowed(r, runState));
    }

    public void Populate(RunState rs, PlayerState player, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(rng);
        if (IsPopulated)
        {
            throw new InvalidOperationException("Grab bag was already populated.");
        }
        var val = new List<string>(RelicCatalog.GetUnlockedRelics(RelicPools.SharedRelicPool, rs.Unlocks));
        val.AddRange(RelicCatalog.GetUnlockedRelics(RelicCatalog.RelicPoolForCharacter(player.CharacterId), rs.Unlocks));
        val.RemoveAll(r => Array.IndexOf(Rarities, RelicCatalog.RarityOf(r)) < 0);
        val = FilterContent(rs, val);
        _originalRelics = val;
        Fill(val, rng);
    }

    public void Populate(IEnumerable<string> relicIds, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(relicIds);
        ArgumentNullException.ThrowIfNull(rng);
        if (IsPopulated)
        {
            throw new InvalidOperationException("Grab bag was already populated.");
        }
        _originalRelics = relicIds.ToList();
        Fill(_originalRelics, rng);
    }

    public string? PullFromFront(RelicRarity rarity, RunState runState) => PullFromFront(rarity, AcceptAll, runState);

    public string? PullFromFront(RelicRarity rarity, Func<string, bool> filter, RunState runState)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var availableDeque = GetAvailableDeque(rarity, runState, filter);
        if (availableDeque == null || availableDeque.Count == 0)
        {
            return null;
        }
        for (int i = 0; i < availableDeque.Count; i++)
        {
            string relic = availableDeque[i];
            if (filter(relic))
            {
                availableDeque.RemoveAt(i);
                return relic;
            }
        }
        return null;
    }

    public string? PullFromBack(RelicRarity rarity, Func<string, bool> filter, RunState runState)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var availableDeque = GetAvailableDeque(rarity, runState, filter);
        if (availableDeque == null || availableDeque.Count == 0)
        {
            return null;
        }
        for (int num = availableDeque.Count - 1; num >= 0; num--)
        {
            string relic = availableDeque[num];
            if (filter(relic))
            {
                availableDeque.RemoveAt(num);
                return relic;
            }
        }
        return null;
    }

    public void Remove(string relicId)
    {
        for (int i = 0; i < _dequeOrder.Count; i++)
        {
            _deques[_dequeOrder[i]].RemoveAll(r => string.Equals(r, relicId, StringComparison.Ordinal));
        }
    }

    public void MoveToFallback(string relicId)
    {
        string? found = null;
        for (int d = 0; d < _dequeOrder.Count; d++)
        {
            var value = _deques[_dequeOrder[d]];
            for (int i = 0; i < value.Count; i++)
            {
                if (string.Equals(value[i], relicId, StringComparison.Ordinal))
                {
                    found ??= value[i];
                    value.RemoveAt(i);
                    i--;
                }
            }
        }
        if (found != null)
        {
            _mpFallbackDequeue.Add(found);
        }
    }

    public bool Contains(string relicId)
    {
        for (int i = 0; i < _dequeOrder.Count; i++)
        {
            if (_deques[_dequeOrder[i]].Contains(relicId, StringComparer.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    public RelicGrabBag DeepClone()
    {
        var clone = new RelicGrabBag(RefreshAllowed);
        for (int i = 0; i < _dequeOrder.Count; i++)
        {
            var rarity = _dequeOrder[i];
            clone._dequeOrder.Add(rarity);
            clone._deques[rarity] = new List<string>(_deques[rarity]);
        }
        clone._mpFallbackDequeue.AddRange(_mpFallbackDequeue);
        clone._originalRelics = _originalRelics;
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.RelicGrabBag);
        w.WriteBool(RefreshAllowed);
        w.WriteCount(_dequeOrder.Count);
        for (int i = 0; i < _dequeOrder.Count; i++)
        {
            var rarity = _dequeOrder[i];
            w.WriteByte((byte)rarity);
            WriteIds(w, _deques[rarity]);
        }
        WriteIds(w, _mpFallbackDequeue);
        var original = _originalRelics;
        w.WriteBool(original != null);
        if (original != null)
        {
            WriteIds(w, original);
        }
    }

    private static void WriteIds(CanonicalWriter w, List<string> ids)
    {
        w.WriteCount(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            w.WriteString(ids[i]);
        }
    }

    private static List<string> FilterContent(RunState rs, IEnumerable<string> ids)
    {
        var content = rs.Config?.Content;
        return content == null ? ids.ToList() : ids.Where(content.AllowsRelic).ToList();
    }

    private void Fill(List<string> relics, Rng rng)
    {
        for (int i = 0; i < relics.Count; i++)
        {
            GetOrAddDeque(RelicCatalog.RarityOf(relics[i])).Add(relics[i]);
        }
        for (int i = 0; i < _dequeOrder.Count; i++)
        {
            _deques[_dequeOrder[i]].UnstableShuffle(rng);
        }
    }

    private List<string> GetOrAddDeque(RelicRarity rarity)
    {
        if (!_deques.TryGetValue(rarity, out var list))
        {
            list = new List<string>();
            _deques[rarity] = list;
            _dequeOrder.Add(rarity);
        }
        return list;
    }

    private List<string>? GetAvailableDeque(RelicRarity rarity, RunState runState, Func<string, bool> filter)
    {
        RemoveDisallowedRelicsFromDeques(runState);
        List<string>? val = GetDeque(rarity);
        if (val.Count == 0 && RefreshAllowed)
        {
            RefreshRarity(rarity);
            RemoveDisallowedRelicsFromDeques(runState);
        }
        while (val != null && !val.Any(filter))
        {
            rarity = rarity switch
            {
                RelicRarity.Shop => RelicRarity.Common,
                RelicRarity.Common => RelicRarity.Uncommon,
                RelicRarity.Uncommon => RelicRarity.Rare,
                _ => RelicRarity.None,
            };
            val = rarity == RelicRarity.None ? null : GetDeque(rarity);
        }
        if (val == null && _mpFallbackDequeue.Any(filter))
        {
            val = _mpFallbackDequeue;
        }
        return val;
    }

    private void RemoveDisallowedRelicsFromDeques(RunState runState)
    {
        for (int d = 0; d < _dequeOrder.Count; d++)
        {
            var value = _deques[_dequeOrder[d]];
            for (int i = 0; i < value.Count; i++)
            {
                if (!RelicCatalog.IsAllowed(value[i], runState))
                {
                    value.RemoveAt(i);
                    i--;
                }
            }
        }
        for (int j = 0; j < _mpFallbackDequeue.Count; j++)
        {
            if (!RelicCatalog.IsAllowed(_mpFallbackDequeue[j], runState))
            {
                _mpFallbackDequeue.RemoveAt(j);
                j--;
            }
        }
    }

    private List<string> GetDeque(RelicRarity rarity) => _deques.TryGetValue(rarity, out var result) ? result : new List<string>();

    private void RefreshRarity(RelicRarity rarity)
    {
        if (_originalRelics == null)
        {
            throw new InvalidOperationException("Tried to refresh relics but original list is null");
        }
        for (int i = 0; i < _originalRelics.Count; i++)
        {
            string current = _originalRelics[i];
            if (RelicCatalog.RarityOf(current) == rarity)
            {
                GetOrAddDeque(rarity).Add(current);
            }
        }
    }
}
