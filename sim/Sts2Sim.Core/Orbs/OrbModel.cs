namespace Sts2Sim.Core.Orbs;

public abstract class OrbModel : AbstractModel, ICanonical
{
    public const int NoOwner = -1;

    public static readonly IReadOnlyList<string> ValidOrbIds = Array.AsReadOnly(new[]
    {
        LightningOrb.OrbIdEntry,
        FrostOrb.OrbIdEntry,
        DarkOrb.OrbIdEntry,
        PlasmaOrb.OrbIdEntry,
        GlassOrb.OrbIdEntry
    });

    private PlayerState? _owner;

    public abstract string Id { get; }

    public PlayerState Owner => _owner ?? throw new InvalidOperationException($"Orb {Id} has no owner");

    public bool HasOwner => _owner != null;

    public bool HasBeenRemovedFromState { get; private set; }

    public abstract decimal PassiveVal(CombatState cs);

    public abstract decimal EvokeVal(CombatState cs);

    public static OrbModel GetRandomOrb(Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return OrbRegistry.Create(rng.NextItem(ValidOrbIds)!);
    }

    public void AttachTo(PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner != null && !ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException("Cannot move orb " + Id + " from one owner to another");
        }
        _owner = owner;
    }

    public virtual void BeforeTurnEndOrbTrigger(CombatState cs)
    {
    }

    public virtual void AfterTurnStartOrbTrigger(CombatState cs)
    {
    }

    public void TriggerPassive(CombatState cs, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        List<AbstractModel>? modifyingModels = null;
        int triggerCount = Hook.ModifyOrbPassiveTriggerCount(cs, this, 1, ref modifyingModels);
        Hook.AfterModifyingOrbPassiveTriggerCount(cs, this, modifyingModels);
        for (int i = 0; i < triggerCount; i++)
        {
            Passive(cs, target);
        }
    }

    public virtual void Passive(CombatState cs, CreatureState? target)
    {
    }

    public virtual IEnumerable<CreatureState> Evoke(CombatState cs) => Array.Empty<CreatureState>();

    protected decimal ModifyOrbValue(CombatState cs, decimal result) => Hook.ModifyOrbValue(cs, this, result);

    public void RemoveInternal()
    {
        HasBeenRemovedFromState = true;
    }

    public OrbModel CloneFor(PlayerState? newOwner)
    {
        var clone = (OrbModel)MemberwiseClone();
        clone._owner = _owner is null ? null : newOwner;
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(OrbModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(OrbCanonicalTags.Orb);
        w.WriteString(Id);
        w.WriteInt32(_owner?.CombatId ?? NoOwner);
        w.WriteBool(HasBeenRemovedFromState);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    protected static List<CreatureState> HittableOpponentsOf(CombatState cs, CreatureState creature)
    {
        var opponents = new List<CreatureState>(4);
        CombatStatus.GetOpponentsOf(cs, creature, opponents);
        var hittable = new List<CreatureState>(opponents.Count);
        for (int i = 0; i < opponents.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, opponents[i]))
            {
                hittable.Add(opponents[i]);
            }
        }
        return hittable;
    }

    protected static List<CreatureState> HittableEnemies(CombatState cs)
    {
        var monsters = cs.Monsters;
        var hittable = new List<CreatureState>(monsters.Count);
        for (int i = 0; i < monsters.Count; i++)
        {
            if (CombatStatus.IsHittable(cs, monsters[i]))
            {
                hittable.Add(monsters[i]);
            }
        }
        return hittable;
    }

    protected static List<PlayerState> PlayersInCombat(CombatState cs)
    {
        var allies = cs.Allies;
        var players = new List<PlayerState>(1);
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player)
            {
                players.Add(player);
            }
        }
        return players;
    }

    public override string ToString() => Id;
}
