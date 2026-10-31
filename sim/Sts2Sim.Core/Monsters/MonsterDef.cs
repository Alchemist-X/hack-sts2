namespace Sts2Sim.Core.Monsters;

public abstract class MonsterExtraState : ICanonical
{
    public abstract MonsterExtraState DeepClone();

    public abstract void WriteCanonical(CanonicalWriter w);
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

    public virtual bool UsesPrivateRng => false;

    public virtual void InitPrivateRng(CombatState cs, MonsterState self, Rng rng)
    {
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

    public virtual bool IsWeak => false;

    public virtual IReadOnlyList<string> Slots => Array.Empty<string>();

    public abstract IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs);

    public virtual IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng) => GenerateMonsters(cs);

    protected InvalidOperationException EncounterRngRequired()
        => new($"Encounter {Id} draws from its private Rng; generate it with GenerateMonsters(cs, rng), as MonsterSpawner.SpawnEncounter does.");
}

public static class MonsterSpawner
{
    public static MonsterState Spawn(CombatState cs, MonsterSpawn spawn) => Spawn(cs, spawn, CombatRunContext.Default);

    public static MonsterState Spawn(CombatState cs, MonsterSpawn spawn, CombatRunContext context)
    {
        var def = spawn.Def;
        var extra = def.CreateExtraState();
        var monster = new MonsterState(cs.AllocateCreatureId(), def, 0, 0, extra, spawn.SlotName);
        spawn.Configure?.Invoke(monster);
        int minInitialHp = def.MinInitialHp(cs, monster);
        int maxInitialHp = def.MaxInitialHp(cs, monster);
        if (minInitialHp > maxInitialHp)
        {
            throw new InvalidOperationException($"{def.Id} has min HP {minInitialHp} greater than its max {maxInitialHp}!");
        }
        monster.MaxHp = maxInitialHp;
        monster.CurrentHp = maxInitialHp;
        int hp = RollUniqueMonsterHp(cs, monster, minInitialHp, maxInitialHp, cs.Rng.Niche);
        monster.MaxHp = hp;
        monster.CurrentHp = hp;
        monster.MaxHpBeforeModification = hp;
        if (def.UsesPrivateRng)
        {
            def.InitPrivateRng(cs, monster, context.CreateMonsterRng(cs.Rng.Seed, monster.CombatId));
        }
        return monster;
    }

    public static void SpawnEncounter(CombatState cs, EncounterDef encounter, List<MonsterState>? spawned = null)
        => SpawnEncounter(cs, encounter, CombatRunContext.Default, spawned);

    public static void SpawnEncounter(CombatState cs, EncounterDef encounter, CombatRunContext context, List<MonsterState>? spawned = null)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        var spawns = encounter.GenerateMonsters(cs, CreateEncounterRng(cs, encounter, context));
        for (int i = 0; i < spawns.Count; i++)
        {
            var monster = Spawn(cs, spawns[i], context);
            cs.Monsters.Add(monster);
            spawned?.Add(monster);
        }
    }

    public static Rng CreateEncounterRng(CombatState cs, EncounterDef encounter, CombatRunContext context)
        => context.CreateEncounterRng(cs.Rng.Seed, encounter.Id);

    public static void SortEnemiesBySlotName(CombatState cs, EncounterDef? encounter)
    {
        if (encounter == null)
        {
            return;
        }
        var slots = encounter.Slots;
        cs.Monsters.Sort((a, b) => IndexOfSlot(slots, a.SlotName) - IndexOfSlot(slots, b.SlotName));
    }

    public static int IndexOfSlot(IReadOnlyList<string> slots, string? slotName)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i], slotName, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    public static int RollUniqueMonsterHp(CombatState cs, MonsterState self, int minInitialHp, int maxInitialHp, Rng rng)
    {
        int num = maxInitialHp + 1;
        int count = num - minInitialHp;
        var candidates = new int[count];
        int available = 0;
        var enemies = cs.Monsters;
        for (int value = minInitialHp; value < num; value++)
        {
            bool taken = false;
            for (int i = 0; i < enemies.Count; i++)
            {
                var other = enemies[i];
                if (!ReferenceEquals(other, self) && other.MaxHp == value)
                {
                    taken = true;
                    break;
                }
            }
            if (!taken)
            {
                candidates[available++] = value;
            }
        }
        if (available <= 0)
        {
            return rng.NextInt(minInitialHp, num);
        }
        return candidates[rng.NextInt(0, available)];
    }
}
