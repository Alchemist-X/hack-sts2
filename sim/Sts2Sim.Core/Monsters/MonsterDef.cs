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

    public abstract IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs);
}

public static class MonsterSpawner
{
    public static MonsterState Spawn(CombatState cs, MonsterSpawn spawn)
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
        return monster;
    }

    public static void SpawnEncounter(CombatState cs, EncounterDef encounter, List<MonsterState>? spawned = null)
    {
        var spawns = encounter.GenerateMonsters(cs);
        for (int i = 0; i < spawns.Count; i++)
        {
            var monster = Spawn(cs, spawns[i]);
            cs.Monsters.Add(monster);
            spawned?.Add(monster);
        }
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
