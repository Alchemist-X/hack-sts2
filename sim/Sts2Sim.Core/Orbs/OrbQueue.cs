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

    public void Clear() => throw new NotImplementedException("OrbQueue.Clear: character-mechanics (Entities.Orbs/OrbQueue.cs:408)");

    public void AddCapacity(int capacity) => throw new NotImplementedException("OrbQueue.AddCapacity: character-mechanics (Entities.Orbs/OrbQueue.cs:414)");

    public void RemoveCapacity(int capacity) => throw new NotImplementedException("OrbQueue.RemoveCapacity: character-mechanics (Entities.Orbs/OrbQueue.cs:419)");

    public bool TryEnqueue(OrbModel orb) => throw new NotImplementedException("OrbQueue.TryEnqueue: character-mechanics (Entities.Orbs/OrbQueue.cs:429)");

    public bool Remove(OrbModel orb) => throw new NotImplementedException("OrbQueue.Remove: character-mechanics (Entities.Orbs/OrbQueue.cs:447)");

    public void Insert(int idx, OrbModel orb) => throw new NotImplementedException("OrbQueue.Insert: character-mechanics (Entities.Orbs/OrbQueue.cs:452)");

    public void BeforeTurnEnd(CombatState cs) => throw new NotImplementedException("OrbQueue.BeforeTurnEnd: character-mechanics (Entities.Orbs/OrbQueue.cs:463)");

    public void AfterTurnStart(CombatState cs) => throw new NotImplementedException("OrbQueue.AfterTurnStart: character-mechanics (Entities.Orbs/OrbQueue.cs:477)");

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
