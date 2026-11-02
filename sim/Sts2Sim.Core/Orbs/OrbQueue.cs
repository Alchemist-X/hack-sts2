namespace Sts2Sim.Core.Orbs;

public sealed class OrbQueue : ICanonical
{
    public const int MaxCapacity = 10;

    private readonly List<OrbModel> _orbs;
    private int _capacity;

    public OrbQueue()
    {
        _orbs = new List<OrbModel>();
    }

    private OrbQueue(List<OrbModel> orbs, int capacity)
    {
        _orbs = orbs;
        _capacity = capacity;
    }

    public IReadOnlyList<OrbModel> Orbs => _orbs;

    public int Capacity => _capacity;

    public void ResetForNewCombat(int baseOrbSlotCount)
    {
        Clear();
        AddCapacity(baseOrbSlotCount);
    }

    public void Clear()
    {
        _orbs.Clear();
        _capacity = 0;
    }

    public void AddCapacity(int capacity)
    {
        _capacity += capacity;
    }

    public void RemoveCapacity(int capacity)
    {
        _capacity = Math.Max(0, _capacity - capacity);
        while (_orbs.Count > _capacity)
        {
            Remove(_orbs[_orbs.Count - 1]);
        }
    }

    public bool TryEnqueue(OrbModel orb)
    {
        if (_capacity == 0)
        {
            return false;
        }
        ArgumentNullException.ThrowIfNull(orb);
        if (_orbs.Count >= _capacity)
        {
            throw new InvalidOperationException("OrbQueue is full");
        }
        _orbs.Add(orb);
        return true;
    }

    public bool Remove(OrbModel orb) => _orbs.Remove(orb);

    public void Insert(int idx, OrbModel orb)
    {
        if (idx >= _capacity)
        {
            throw new InvalidOperationException("idx cannot be greater than capacity");
        }
        _orbs.Insert(idx, orb);
    }

    public void BeforeTurnEnd(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        if (_orbs.Count == 0)
        {
            return;
        }
        var orbs = _orbs.ToArray();
        for (int i = 0; i < orbs.Length; i++)
        {
            if (!IsOwnerInCombat(cs))
            {
                return;
            }
            orbs[i].BeforeTurnEndOrbTrigger(cs);
        }
    }

    public void AfterTurnStart(CombatState cs)
    {
        ArgumentNullException.ThrowIfNull(cs);
        if (_orbs.Count == 0)
        {
            return;
        }
        var orbs = _orbs.ToArray();
        for (int i = 0; i < orbs.Length; i++)
        {
            if (!IsOwnerInCombat(cs))
            {
                return;
            }
            orbs[i].AfterTurnStartOrbTrigger(cs);
        }
    }

    private bool IsOwnerInCombat(CombatState cs)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player && ReferenceEquals(player.OrbQueue, this))
            {
                return true;
            }
        }
        return false;
    }

    public OrbQueue CloneFor(PlayerState? newOwner)
    {
        var source = _orbs;
        var orbs = new List<OrbModel>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            orbs.Add(source[i].CloneFor(newOwner));
        }
        return new OrbQueue(orbs, _capacity);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(OrbCanonicalTags.Queue);
        w.WriteInt32(_capacity);
        var orbs = _orbs;
        w.WriteCount(orbs.Count);
        for (int i = 0; i < orbs.Count; i++)
        {
            orbs[i].WriteCanonical(w);
        }
    }
}
