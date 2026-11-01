namespace Sts2Sim.Core.Monsters;

public abstract class MonsterExtraState : ICanonical
{
    public abstract MonsterExtraState DeepClone();

    public abstract void WriteCanonical(CanonicalWriter w);
}

public abstract class PrivateRngExtraState : MonsterExtraState
{
    public Rng? PrivateRng { get; set; }

    public sealed override MonsterExtraState DeepClone()
    {
        var clone = CloneFields();
        clone.PrivateRng = PrivateRng?.DeepClone();
        return clone;
    }

    public sealed override void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.PrivateRngExtra);
        var rng = PrivateRng;
        w.WriteBool(rng != null);
        rng?.WriteCanonical(w);
        WriteFieldsCanonical(w);
    }

    protected abstract PrivateRngExtraState CloneFields();

    protected abstract void WriteFieldsCanonical(CanonicalWriter w);
}

public abstract class MonsterDef
{
    protected MonsterDef(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public abstract int MinInitialHp(CombatState cs);

    public abstract int MaxInitialHp(CombatState cs);

    public virtual int MinInitialHp(CombatState cs, MonsterState self) => MinInitialHp(cs);

    public virtual int MaxInitialHp(CombatState cs, MonsterState self) => MaxInitialHp(cs);

    public virtual MonsterExtraState? CreateExtraState() => null;

    public abstract MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self);

    public virtual void AfterAddedToRoom(CombatState cs, MonsterState self)
    {
    }

    public virtual void BeforeRemovedFromRoom(CombatState cs, MonsterState self)
    {
    }

    public virtual void AfterCardChangedPilesLate(RunState rs, MonsterState self, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
    {
    }

    public virtual void AfterCardGeneratedForCombat(CombatState cs, MonsterState self, CardInstance card, PlayerState? creator)
    {
    }

    public virtual void AfterCurrentHpChanged(RunState rs, MonsterState self, CreatureState creature, decimal delta)
    {
    }

    public virtual void AfterDamageReceived(RunState rs, MonsterState self, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
    }

    public virtual void BeforeDeath(RunState rs, MonsterState self, CreatureState creature)
    {
    }

    public virtual void AfterDeath(RunState rs, MonsterState self, CreatureState creature, bool wasRemovalPrevented)
    {
    }

    public virtual bool UsesPrivateRng => false;

    public virtual void InitPrivateRng(CombatState cs, MonsterState self, Rng rng)
    {
        if (self.Extra is not PrivateRngExtraState holder)
        {
            throw new InvalidOperationException($"{Id} uses a private Rng, so its extra state must derive from {nameof(PrivateRngExtraState)}, which clones and hashes it.");
        }
        holder.PrivateRng = rng;
    }
}

public sealed record MonsterSpawn(MonsterDef Def, string? SlotName = null, Action<MonsterState>? Configure = null);

public abstract class EncounterDef
{
    protected EncounterDef(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public virtual RoomType RoomType => RoomType.Monster;

    public virtual bool IsWeak => false;

    public virtual bool ShouldGiveRewards => true;

    public virtual IReadOnlyList<EncounterTag> Tags => Array.Empty<EncounterTag>();

    public virtual IReadOnlyList<string> AllPossibleMonsters => Array.Empty<string>();

    public virtual IReadOnlyList<string> Slots => Array.Empty<string>();

    public abstract IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs);

    public virtual IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng) => GenerateMonsters(cs);

    public string GetNextSlot(CombatState cs)
    {
        var slots = Slots;
        var enemies = cs.Monsters;
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            bool free = true;
            for (int j = 0; j < enemies.Count; j++)
            {
                if (string.Equals(enemies[j].SlotName, slot, StringComparison.Ordinal))
                {
                    free = false;
                    break;
                }
            }
            if (free)
            {
                return slot;
            }
        }
        return string.Empty;
    }

    protected InvalidOperationException EncounterRngRequired()
        => new($"Encounter {Id} draws from its private Rng; generate it with GenerateMonsters(cs, rng), as MonsterSpawner.SpawnEncounter does.");
}

public enum EncounterTag
{
    None,
    Burrower,
    Chomper,
    Nibbit,
    Shrinker,
    Slimes,
    Thieves,
    Workers,
    Crawler,
    Mushroom,
    Knights,
    Scrolls,
    Seapunk,
    Slugs,
    Exoskeletons,
    Jaxfruit
}
