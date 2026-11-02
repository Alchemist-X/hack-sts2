namespace Sts2Sim.Core.Monsters;

public static class MonsterSpawner
{
    public const string UnknownRunContextSite = "MonsterSpawner.CreateCreature:MonsterModel.Rng seeded with CombatRunContext.Default (no map coordinate or act index on the combat; Combat/CombatState.cs:721)";

    public static MonsterState Spawn(CombatState cs, MonsterSpawn spawn) => Spawn(cs, spawn, CombatRunContext.Default);

    public static MonsterState Spawn(CombatState cs, MonsterSpawn spawn, CombatRunContext context)
        => CreateCreature(cs, spawn.Def, CombatSide.Enemy, spawn.SlotName, spawn.Configure, context);

    public static MonsterState CreateCreature(CombatState cs, MonsterDef def, CombatSide side, string? slotName)
        => CreateCreature(cs, def, side, slotName, null, null);

    public static MonsterState CreateCreature(CombatState cs, MonsterDef def, CombatSide side, string? slotName, Action<MonsterState>? configure, CombatRunContext? context)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(def);
        var extra = def.CreateExtraState();
        var monster = new MonsterState(cs.AllocateCreatureId(), def, 0, 0, extra, slotName);
        if (side != CombatSide.Enemy)
        {
            monster.Side = side;
        }
        configure?.Invoke(monster);
        int minInitialHp = def.MinInitialHp(cs, monster);
        int maxInitialHp = def.MaxInitialHp(cs, monster);
        if (minInitialHp > maxInitialHp)
        {
            throw new InvalidOperationException($"{def.Id} has min HP {minInitialHp} greater than its max {maxInitialHp}!");
        }
        monster.MaxHp = maxInitialHp;
        monster.CurrentHp = maxInitialHp;
        if (side == CombatSide.Enemy)
        {
            int hp = RollUniqueMonsterHp(cs, monster, minInitialHp, maxInitialHp, cs.Rng.Niche);
            monster.MaxHp = hp;
            monster.CurrentHp = hp;
            monster.MaxHpBeforeModification = hp;
        }
        if (def.UsesPrivateRng)
        {
            if (context is not { } known)
            {
                cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, UnknownRunContextSite);
                known = CombatRunContext.Default;
            }
            def.InitPrivateRng(cs, monster, known.CreateMonsterRng(cs.Rng.Seed, monster.CombatId));
        }
        cs.OnCreatureSpawned(monster);
        return monster;
    }

    public static MonsterState SpawnForAdd(CombatState cs, MonsterDef def, CombatSide side, string? slotName)
        => SpawnForAdd(cs, def, side, slotName, null);

    public static MonsterState SpawnForAdd(CombatState cs, MonsterDef def, CombatSide side, string? slotName, Action<MonsterState>? configure)
    {
        var monster = CreateCreature(cs, def, side, slotName, configure, null);
        AddToCombat(cs, monster);
        return monster;
    }

    public static MonsterState SpawnForAdd(CombatState cs, MonsterDef def, CombatSide side, string? slotName, Action<MonsterState>? configure, EncounterDef? encounter)
    {
        var monster = CreateCreature(cs, def, side, slotName, configure, null);
        AddToCombat(cs, monster, encounter);
        return monster;
    }

    public static void AddToCombat(CombatState cs, MonsterState monster) => AddToCombat(cs, monster, EncounterOf(cs));

    public static void AddToCombat(CombatState cs, MonsterState monster, EncounterDef? encounter)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(monster);
        if (!cs.IsInProgress)
        {
            throw new InvalidOperationException("Attempted to add a creature outside of combat.");
        }
        AddCreature(cs, monster);
        MonsterLifecycle.SetUpForCombat(cs, monster);
        if (monster.SlotName != null)
        {
            SortEnemiesBySlotName(cs, encounter);
        }
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        if (cs.CurrentSide != CombatSide.Enemy)
        {
            MonsterLifecycle.PrepareForNextTurn(cs, monster, rollNewMove: false);
        }
        CreatureCmd.AppendToRoomHistory(cs, monster);
        Hook.AfterCreatureAddedToCombat(cs, monster);
    }

    public static MonsterState SpawnPet(CombatState cs, MonsterDef def, PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var pet = CreateCreature(cs, def, owner.Side, null, null, null);
        AddPet(cs, pet, owner);
        return pet;
    }

    public static void AddPet(CombatState cs, MonsterState pet, PlayerState owner)
    {
        AddPetInternal(owner, pet);
        AddToCombat(cs, pet);
    }

    public static void AddPetInternal(PlayerState owner, MonsterState pet)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(pet);
        var pets = owner.PetIds;
        if (pets.Contains(pet.CombatId))
        {
            return;
        }
        if (pet.PetOwnerId != owner.CombatId)
        {
            if (pet.PetOwnerId != PowerModel.NoCreature)
            {
                throw new InvalidOperationException($"Pet {pet.MonsterId} ({pet.CombatId}) already has an owner.");
            }
            pet.PetOwnerId = owner.CombatId;
        }
        pets.Add(pet.CombatId);
    }

    public static EncounterDef? EncounterOf(CombatState cs)
        => EncounterRegistry.TryGet(cs.EncounterId, out var encounter) ? encounter : null;

    public static void SetEnemyIndex(CombatState cs, MonsterState creature, int index) => SetEnemyIndex(cs, EncounterOf(cs), creature, index);

    public static void SetEnemyIndex(CombatState cs, EncounterDef? encounter, MonsterState creature, int index)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (encounter.Slots.Count > 0)
        {
            throw new InvalidOperationException("Cannot modify turn order of a combat with pre-set slots");
        }
        var enemies = cs.Monsters;
        int at = IndexOfReference(enemies, creature);
        if (at < 0)
        {
            throw new ArgumentException("Creature must be a valid enemy to change its turn order.");
        }
        enemies.RemoveAt(at);
        enemies.Insert(Math.Min(index, enemies.Count - 1), creature);
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

    private static void AddCreature(CombatState cs, MonsterState monster)
    {
        if (IndexOfReference(cs.Monsters, monster) >= 0 || IndexOfReference(cs.Allies, monster) >= 0)
        {
            throw new InvalidOperationException("Creature is already in this combat, but AddCreature was called on it again.");
        }
        if (monster.Side == CombatSide.Player)
        {
            cs.AddAlly(monster);
        }
        else
        {
            cs.AddMonster(monster);
        }
    }

    internal static int IndexOfReference<T>(IReadOnlyList<T> list, CreatureState creature) where T : CreatureState
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], creature))
            {
                return i;
            }
        }
        return -1;
    }
}
