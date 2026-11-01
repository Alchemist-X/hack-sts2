using System.Reflection;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class MonsterKernelTests
{
    private const string TestEncounterId = "MONSTER_KERNEL_TEST_ENCOUNTER";
    private const string ProbeId = "MONSTER_KERNEL_PROBE";
    private const string WaitMoveId = "MONSTER_KERNEL_WAIT";
    private const string MoveA = "MONSTER_KERNEL_A";
    private const string MoveB = "MONSTER_KERNEL_B";
    private const string BranchId = "MONSTER_KERNEL_BRANCH";

    private static readonly string[] RoutedHooks =
    {
        "AfterCardChangedPilesLate", "AfterCardGeneratedForCombat", "AfterCurrentHpChanged", "AfterDamageReceived", "AfterDeath", "BeforeDeath"
    };

    public static void Test_MonsterKernel_SetUpForCombatInstallsAnOwnedListener()
    {
        var log = new List<string>();
        var cs = NewCombat();
        var monster = AddEnemy(cs, new ProbeDef(log));
        var listener = Assert.NotNull(monster.Listener as MonsterListener, "SetUpForCombat installs a MonsterListener");
        Assert.True(ReferenceEquals(monster, listener.Owner), "the listener stores its owner");
        Assert.True(ReferenceEquals(monster.Def, listener.Def));
        Assert.True(listener is IOwnedListener<MonsterState>, "the adapter is rebound on clone");
        Assert.True(monster.HasAi);
        Assert.True(monster.SpawnedThisTurn);
        Assert.SequenceEqual(new[] { "graph:1" }, log);
        Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.SetUpForCombat(cs, monster), "a second SetUpForCombat throws, as the game's MoveStateMachine setter does");
    }

    public static void Test_MonsterKernel_ListenerOverridesExactlyTheSixMonsterHooks()
    {
        var overridden = typeof(MonsterListener)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetBaseDefinition().DeclaringType == typeof(AbstractModel))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.SequenceEqual(RoutedHooks, overridden);
        foreach (var name in RoutedHooks)
        {
            var defVirtual = typeof(MonsterDef).GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.True(defVirtual != null && defVirtual.IsVirtual && defVirtual.GetParameters()[1].ParameterType == typeof(MonsterState), name + " is a MonsterDef virtual taking its monster second");
        }
        foreach (var name in new[] { "BeforeRemovedFromRoom", "AfterAddedToRoom" })
        {
            var lifecycle = typeof(MonsterDef).GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.True(lifecycle != null && lifecycle.IsVirtual, name + " is a MonsterDef virtual");
        }
        Assert.Equal(0, typeof(MonsterDef).GetMember("ShouldDisappearFromDoom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length, "VFX only, not ported");
        Assert.Equal(0, typeof(MonsterDef).GetMember("OnDieToDoom", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length, "VFX only, not ported");
    }

    public static void Test_MonsterKernel_ListenerRoutesEachHookToTheDefWithItsOwner()
    {
        var log = new List<string>();
        var cs = NewCombat();
        var monster = AddEnemy(cs, new ProbeDef(log));
        var other = AddEnemy(cs, new ProbeDef(log));
        log.Clear();
        var listener = monster.Listener!;
        var rs = cs.Run;
        var card = cs.CreateCard(CardRegistry.Get("STRIKE_IRONCLAD"), cs.Player);
        var result = new DamageResult(other, ValueProp.Move) { UnblockedDamage = 3 };
        listener.AfterCardChangedPilesLate(rs, card, PileType.Draw, null);
        listener.AfterCardGeneratedForCombat(cs, card, cs.Player);
        listener.AfterCurrentHpChanged(rs, other, -3m);
        listener.AfterDamageReceived(rs, other, result, ValueProp.Move, cs.Player, card);
        listener.BeforeDeath(rs, other);
        listener.AfterDeath(rs, other, true);
        Assert.SequenceEqual(new[]
        {
            $"piles-late:1:{card.InstanceId}:Draw",
            $"generated:1:{card.InstanceId}:0",
            "hp-changed:1:2:-3",
            $"damage-received:1:2:3:Move:0:{card.InstanceId}",
            "before-death:1:2",
            "after-death:1:2:True"
        }, log);

        log.Clear();
        listener.AfterSideTurnStart(cs, CombatSide.Player, Array.Empty<CreatureState>());
        listener.AfterCreatureAddedToCombat(cs, other);
        Assert.True(listener.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, other), "hooks monsters do not override keep the AbstractModel default");
        Assert.Equal(0, log.Count, "only the six monster hooks reach the def");
    }

    public static void Test_MonsterKernel_ListenerCloneRoutesToTheClonedMonsterAndHashesEqual()
    {
        var log = new List<string>();
        var cs = NewCombat();
        var monster = AddEnemy(cs, new ProbeDef(log));
        var clone = cs.DeepClone();
        var cloneMonster = clone.Monsters[0];
        var cloneListener = Assert.NotNull(cloneMonster.Listener as MonsterListener);
        Assert.False(ReferenceEquals(monster.Listener, cloneListener), "the clone owns its listener");
        Assert.True(ReferenceEquals(cloneMonster, cloneListener.Owner), "the clone's listener routes to the cloned monster");
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone));
        Assert.SequenceEqual(Canonical(cs), Canonical(clone));

        log.Clear();
        cloneListener.AfterDeath(clone.Run, cloneMonster, false);
        monster.Listener!.AfterDeath(cs.Run, monster, false);
        Assert.SequenceEqual(new[] { "after-death:1:1:False", "after-death:1:1:False" }, log);

        var direct = monster.DeepClone();
        Assert.True(ReferenceEquals(direct, ((MonsterListener)direct.Listener!).Owner), "MonsterState.DeepClone rebinds through IOwnedListener");
        Assert.Equal(StateHasher.Hash(monster), StateHasher.Hash(direct));

        ulong sourceHash = StateHasher.Hash(cs);
        cloneMonster.CurrentHp -= 1;
        Assert.Equal(sourceHash, StateHasher.Hash(cs), "mutating the clone leaves the source hash unchanged");
        Assert.NotEqual(sourceHash, StateHasher.Hash(clone));
    }

    public static void Test_MonsterKernel_ListenerFollowsItsCreaturesPowersInDispatchOrder()
    {
        W1Pending.Run(nameof(Test_MonsterKernel_ListenerFollowsItsCreaturesPowersInDispatchOrder), "hooks", () =>
        {
            var log = new List<string>();
            var cs = NewCombat();
            var def = new ProbeDef(log);
            var first = AddEnemy(cs, def);
            var second = AddEnemy(cs, def);
            var p = new LogPower(log, "P");
            var a = new LogPower(log, "A");
            var b = new LogPower(log, "B");
            p.ApplyInternal(cs.Player, 1m);
            a.ApplyInternal(first, 1m);
            b.ApplyInternal(second, 1m);
            var buffer = new List<AbstractModel>();
            Hook.IterateHookListeners(cs, buffer);
            RequireMonsterListenerSlot(buffer, first);
            Assert.SequenceEqual(new AbstractModel[] { p, a, first.Listener!, b, second.Listener! }, buffer, "per creature its powers then its monster, allies before enemies");

            log.Clear();
            Hook.AfterDeath(cs, first, false);
            Assert.SequenceEqual(new[] { "P:after-death:1", "A:after-death:1", "after-death:1:1:False", "B:after-death:1", "after-death:2:1:False" }, log);

            first.CurrentHp = 0;
            Hook.IterateHookListeners(cs, buffer);
            Assert.True(buffer.Contains(first.Listener!), "a dead monster still in the combat stays a listener (Creature.CombatState != null)");
            cs.Monsters.Remove(second);
            Hook.IterateHookListeners(cs, buffer);
            Assert.False(buffer.Contains(second.Listener!), "a monster removed from the combat is no longer a listener");
        });
    }

    public static void Test_MonsterKernel_SpawnForAddFollowsTheGameOrder()
    {
        var log = new List<string>();
        var cs = NewCombat(31UL);
        var def = new ProbeDef(log, 10, 12, randomGraph: true);
        var existing = AddEnemy(cs, def);
        existing.MaxHp = 11;
        existing.CurrentHp = 11;
        var power = new LogPower(log, "P");
        power.ApplyInternal(cs.Player, 1m);
        log.Clear();

        int expectedId = cs.NextCreatureId;
        var niche = cs.Rng.Niche.DeepClone();
        var candidates = new[] { 10, 12 };
        int expectedHp = candidates[niche.NextInt(0, candidates.Length)];
        var before = Counters(cs);

        var added = MonsterSpawner.SpawnForAdd(cs, def, CombatSide.Enemy, null);

        Assert.Equal(expectedId, added.CombatId, "the next creature id");
        Assert.Equal(expectedId + 1, cs.NextCreatureId);
        Assert.Equal(expectedHp, added.MaxHp, "unique HP among the enemies, drawn on Niche");
        Assert.Equal(expectedHp, added.CurrentHp);
        Assert.Equal(expectedHp, added.MaxHpBeforeModification);
        Assert.Equal(CombatSide.Enemy, added.Side);
        AssertAdvanced(before, Counters(cs), (RunRngType.Niche, 1), (RunRngType.MonsterAi, 1));
        Assert.True(ReferenceEquals(added, cs.Monsters[^1]), "appended to the enemies");
        Assert.SequenceEqual(new[] { $"graph:{expectedId}", $"added:{expectedId}", $"P:creature-added:{expectedId}" }, log, "SetUpForCombat, AfterAddedToRoom, then AfterCreatureAddedToCombat");
        Assert.NotNull(added.Ai.NextMove, "the first move is rolled on the player side");
        Assert.True(added.SpawnedThisTurn);
        Assert.True(added.Listener is MonsterListener);
    }

    public static void Test_MonsterKernel_SpawnForAddOnTheEnemySideRollsNoMove()
    {
        var log = new List<string>();
        var cs = NewCombat(32UL);
        cs.CurrentSide = CombatSide.Enemy;
        var def = new ProbeDef(log, 10, 12, randomGraph: true);
        var before = Counters(cs);
        var added = MonsterSpawner.SpawnForAdd(cs, def, CombatSide.Enemy, null);
        AssertAdvanced(before, Counters(cs), (RunRngType.Niche, 1));
        Assert.Null(added.Ai.NextMove, "AfterCreatureAdded rolls only while the player side acts");
        Assert.SequenceEqual(new[] { $"graph:{added.CombatId}", $"added:{added.CombatId}" }, log);
    }

    public static void Test_MonsterKernel_AddToCombatRequiresAnInProgressCombat()
    {
        var log = new List<string>();
        var cs = NewCombat(33UL);
        cs.IsInProgress = false;
        int id = cs.NextCreatureId;
        var before = Counters(cs);
        var thrown = Assert.Throws<InvalidOperationException>(() => MonsterSpawner.SpawnForAdd(cs, new ProbeDef(log), CombatSide.Enemy, null));
        Assert.Equal("Attempted to add a creature outside of combat.", thrown.Message);
        Assert.Equal(id + 1, cs.NextCreatureId, "CreateCreature runs before the in-progress check");
        AssertAdvanced(before, Counters(cs), (RunRngType.Niche, 1));
        Assert.Equal(0, cs.Monsters.Count);
        Assert.Equal(0, log.Count);

        cs.IsInProgress = true;
        var monster = MonsterSpawner.SpawnForAdd(cs, new ProbeDef(log), CombatSide.Enemy, null);
        var duplicate = Assert.Throws<InvalidOperationException>(() => MonsterSpawner.AddToCombat(cs, monster));
        Assert.Equal("Creature is already in this combat, but AddCreature was called on it again.", duplicate.Message);
    }

    public static void Test_MonsterKernel_SpawnForAddWithASlotSortsTheEnemies()
    {
        var log = new List<string>();
        var cs = NewCombat(34UL);
        var encounter = new SlottedEncounter("front", "middle", "back");
        var def = new ProbeDef(log);
        AddEnemy(cs, def, "front");
        AddEnemy(cs, def, "back");
        Assert.Equal("middle", encounter.GetNextSlot(cs), "the first slot no enemy holds");
        var middle = MonsterSpawner.SpawnForAdd(cs, def, CombatSide.Enemy, encounter.GetNextSlot(cs), null, encounter);
        Assert.SequenceEqual(new[] { "front", "middle", "back" }, cs.Monsters.Select(m => m.SlotName), "CombatManager.AddCreature sorts by slot after SetUpForCombat");
        Assert.True(ReferenceEquals(middle, cs.Monsters[1]));
        Assert.Equal(string.Empty, encounter.GetNextSlot(cs), "no free slot gives the empty string");
        Assert.Equal(string.Empty, new SlottedEncounter().GetNextSlot(cs), "an encounter without slots gives the empty string");

        var unsorted = NewCombat(35UL);
        AddEnemy(unsorted, def, "back");
        MonsterSpawner.SpawnForAdd(unsorted, def, CombatSide.Enemy, null, null, encounter);
        Assert.SequenceEqual(new[] { "back", null }, unsorted.Monsters.Select(m => m.SlotName), "a creature without a slot triggers no sort");
    }

    public static void Test_MonsterKernel_SetEnemyIndexForSlotlessEncounters()
    {
        var log = new List<string>();
        var def = new ProbeDef(log);
        var slotless = new SlottedEncounter();
        var cs = NewCombat(36UL);
        var a = AddEnemy(cs, def);
        var b = AddEnemy(cs, def);
        var c = AddEnemy(cs, def);
        MonsterSpawner.SetEnemyIndex(cs, slotless, a, 0);
        Assert.SequenceEqual(new[] { a, b, c }, cs.Monsters);
        MonsterSpawner.SetEnemyIndex(cs, slotless, a, 5);
        Assert.SequenceEqual(new[] { b, a, c }, cs.Monsters, "the index is clamped to Count - 1 after the removal: second to last, never last");
        MonsterSpawner.SetEnemyIndex(cs, slotless, c, 0);
        Assert.SequenceEqual(new[] { c, b, a }, cs.Monsters);
        MonsterSpawner.SetEnemyIndex(cs, slotless, c, 1);
        Assert.SequenceEqual(new[] { b, c, a }, cs.Monsters);

        var slotted = Assert.Throws<InvalidOperationException>(() => MonsterSpawner.SetEnemyIndex(cs, new SlottedEncounter("front"), a, 0));
        Assert.Equal("Cannot modify turn order of a combat with pre-set slots", slotted.Message);
        var stranger = MonsterSpawner.CreateCreature(cs, def, CombatSide.Enemy, null);
        var notEnemy = Assert.Throws<ArgumentException>(() => MonsterSpawner.SetEnemyIndex(cs, slotless, stranger, 0));
        Assert.Equal("Creature must be a valid enemy to change its turn order.", notEnemy.Message);
        Assert.Throws<ArgumentNullException>(() => MonsterSpawner.SetEnemyIndex(cs, null, a, 0), "the game reads Encounter.Slots without a null check");
        Assert.SequenceEqual(new[] { b, c, a }, cs.Monsters, "failed calls leave the order unchanged");

        var single = NewCombat(37UL);
        var only = AddEnemy(single, def);
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterSpawner.SetEnemyIndex(single, slotless, only, 0), "Insert(Math.Min(index, -1)) after the removal");
        Assert.Equal(0, single.Monsters.Count, "the creature was removed before the insert threw");
    }

    public static void Test_MonsterKernel_CreateCreatureOnThePlayerSideRollsNoHp()
    {
        var log = new List<string>();
        var cs = NewCombat(38UL);
        AddEnemy(cs, new ProbeDef(log, 10, 12));
        int id = cs.NextCreatureId;
        var before = Counters(cs);
        var pet = MonsterSpawner.CreateCreature(cs, new ProbeDef(log, 20, 25), CombatSide.Player, null);
        Assert.Equal(id, pet.CombatId);
        Assert.Equal(CombatSide.Player, pet.Side);
        Assert.Equal(25, pet.MaxHp, "a player-side creature keeps MaxInitialHp");
        Assert.Equal(25, pet.CurrentHp);
        Assert.Null(pet.MaxHpBeforeModification, "only the enemy roll sets MonsterMaxHpBeforeModification");
        AssertAdvanced(before, Counters(cs));
        Assert.Equal(PowerModel.NoCreature, pet.PetOwnerId);
        Assert.False(pet.HasAi, "CreateCreature does not set the creature up");
        Assert.Null(pet.Listener);
        Assert.False(cs.Monsters.Contains(pet));
        Assert.False(cs.Allies.Contains(pet));
    }

    public static void Test_MonsterKernel_AddPetInternalSetsTheOwnerOnce()
    {
        var log = new List<string>();
        var cs = NewCombat(39UL);
        var owner = cs.Player;
        var pet = MonsterSpawner.CreateCreature(cs, new ProbeDef(log), CombatSide.Player, null);
        MonsterSpawner.AddPetInternal(owner, pet);
        Assert.Equal(owner.CombatId, pet.PetOwnerId);
        Assert.SequenceEqual(new[] { pet.CombatId }, owner.PetIds);
        MonsterSpawner.AddPetInternal(owner, pet);
        Assert.SequenceEqual(new[] { pet.CombatId }, owner.PetIds, "a registered pet is not added twice");

        owner.PetIds.Clear();
        MonsterSpawner.AddPetInternal(owner, pet);
        Assert.SequenceEqual(new[] { pet.CombatId }, owner.PetIds, "the same owner re-adds a pet it lost (OstyCmd revive path)");

        var stranger = new PlayerState(99, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var thrown = Assert.Throws<InvalidOperationException>(() => MonsterSpawner.AddPetInternal(stranger, pet));
        Assert.True(thrown.Message.Contains("already has an owner", StringComparison.Ordinal));
        Assert.Equal(0, stranger.PetIds.Count);
        Assert.Equal(owner.CombatId, pet.PetOwnerId);
    }

    public static void Test_MonsterKernel_PetsNeverEnterTheRoomNorRollNorTakeTurns()
    {
        var log = new List<string>();
        var cs = NewCombat(40UL);
        var pet = MonsterSpawner.CreateCreature(cs, new ProbeDef(log, randomGraph: true), CombatSide.Player, null);
        MonsterLifecycle.SetUpForCombat(cs, pet);
        log.Clear();
        var before = Counters(cs);
        MonsterLifecycle.AfterCreatureAdded(cs, pet);
        Assert.Equal(0, log.Count, "no AfterAddedToRoom off the enemy side");
        Assert.Null(pet.Ai.NextMove, "no RollMove for a creature that is not an enemy");
        AssertAdvanced(before, Counters(cs));
        pet.SpawnedThisTurn = false;
        var thrown = Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.TakeTurn(cs, pet));
        Assert.Equal("Only enemy monsters can take automated turns.", thrown.Message);
    }

    public static void Test_MonsterKernel_PetDeathDropsThePetUnlessAListenerKeepsIt()
    {
        var log = new List<string>();
        var cs = NewCombat(41UL);
        var owner = cs.Player;
        var pet = MonsterSpawner.CreateCreature(cs, new ProbeDef(log), CombatSide.Player, null);
        MonsterSpawner.AddPetInternal(owner, pet);
        var keeper = new LogPower(log, "K") { KeepDeadCreatures = true };
        keeper.ApplyInternal(owner, 1m);
        MonsterLifecycle.InvokeDiedEvent(cs, pet);
        Assert.SequenceEqual(new[] { pet.CombatId }, owner.PetIds, "ShouldCreatureBeRemovedFromCombatAfterDeath false keeps the pet registered");
        keeper.KeepDeadCreatures = false;
        MonsterLifecycle.InvokeDiedEvent(cs, pet);
        Assert.Equal(0, owner.PetIds.Count, "the default drops the pet from Pets");
        MonsterLifecycle.InvokeDiedEvent(cs, pet);
        Assert.Equal(0, owner.PetIds.Count, "an unregistered pet is no longer subscribed");
        Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.OnPetDied(cs, owner, pet), "OnPetDied requires a registered pet");

        var enemy = AddEnemy(cs, new ProbeDef(log));
        MonsterLifecycle.InvokeDiedEvent(cs, enemy);
        MonsterLifecycle.InvokeDiedEvent(cs, owner);
        Assert.Equal(PowerModel.NoCreature, enemy.PetOwnerId);
    }

    public static void Test_MonsterKernel_SpawnPetFollowsTheAddPetOrder()
    {
        W1Pending.Run(nameof(Test_MonsterKernel_SpawnPetFollowsTheAddPetOrder), "state", () =>
        {
            var log = new List<string>();
            var cs = NewCombat(42UL);
            var power = new LogPower(log, "P");
            power.ApplyInternal(cs.Player, 1m);
            int id = cs.NextCreatureId;
            var before = Counters(cs);
            var pet = MonsterSpawner.SpawnPet(cs, new ProbeDef(log, 6, 9, randomGraph: true), cs.Player);
            Assert.Equal(id, pet.CombatId);
            Assert.Equal(CombatSide.Player, pet.Side);
            Assert.Equal(9, pet.MaxHp);
            Assert.Equal(9, pet.CurrentHp);
            Assert.Null(pet.MaxHpBeforeModification);
            AssertAdvanced(before, Counters(cs));
            Assert.Equal(cs.Player.CombatId, pet.PetOwnerId);
            Assert.SequenceEqual(new[] { pet.CombatId }, cs.Player.PetIds);
            Assert.SequenceEqual(new CreatureState[] { cs.Player, pet }, cs.Allies, "pets follow the player in insertion order");
            Assert.False(cs.Monsters.Contains(pet));
            Assert.True(pet.HasAi && pet.SpawnedThisTurn && pet.Listener is MonsterListener, "CombatManager.AddCreature set the pet up");
            Assert.Null(pet.Ai.NextMove);
            Assert.SequenceEqual(new[] { $"graph:{id}", $"P:creature-added:{id}" }, log, "no AfterAddedToRoom, then AfterCreatureAddedToCombat");
            var thrown = Assert.Throws<InvalidOperationException>(() => MonsterSpawner.AddToCombat(cs, pet));
            Assert.Equal("Creature is already in this combat, but AddCreature was called on it again.", thrown.Message);
        });
    }

    public static void Test_MonsterKernel_DeadPetsStayInAlliesAndLeaveThePetList()
    {
        W1Pending.Run(nameof(Test_MonsterKernel_DeadPetsStayInAlliesAndLeaveThePetList), "state, pipeline", () =>
        {
            var log = new List<string>();
            var cs = NewCombat(43UL);
            AddEnemy(cs, new ProbeDef(log));
            var pet = MonsterSpawner.SpawnPet(cs, new ProbeDef(log), cs.Player);
            CreatureCmd.Kill(cs, pet);
            if (pet.IsAlive)
            {
                throw new NotImplementedException("pipeline: CreatureCmd.Kill does not yet treat a pet in cs.Allies as attached (CONTRACTS Part B section B12 A19)");
            }
            if (cs.Player.PetIds.Contains(pet.CombatId))
            {
                throw new NotImplementedException("pipeline: CreatureCmd.Kill does not yet raise the Died event through MonsterLifecycle.InvokeDiedEvent (Commands/CreatureCmd.cs:2770)");
            }
            Assert.True(cs.Allies.Contains(pet), "Kill removes only enemy-side creatures");
            Assert.True(cs.Player.IsAlive);
            Assert.Null(MonsterLifecycle.GetPet(cs, cs.Player, ProbeId), "a pet that left Pets is not found");
        });
    }

    public static void Test_MonsterKernel_GetPetResolvesRegisteredPetsInOrder()
    {
        W1Pending.Run(nameof(Test_MonsterKernel_GetPetResolvesRegisteredPetsInOrder), "state", () =>
        {
            var log = new List<string>();
            var cs = NewCombat(44UL);
            var first = MonsterSpawner.SpawnPet(cs, new ProbeDef(log), cs.Player);
            var second = MonsterSpawner.SpawnPet(cs, new ProbeDef(log), cs.Player);
            Assert.True(ReferenceEquals(first, MonsterLifecycle.GetPet(cs, cs.Player, ProbeId)), "the first registered pet with that id");
            Assert.Null(MonsterLifecycle.GetPet(cs, cs.Player, "OSTY"));
            var buffer = new List<MonsterState>();
            MonsterLifecycle.GetPets(cs, cs.Player, buffer);
            Assert.SequenceEqual(new[] { first, second }, buffer);
            cs.Player.PetIds.Remove(first.CombatId);
            Assert.True(ReferenceEquals(second, MonsterLifecycle.GetPet(cs, cs.Player, ProbeId)));
        });
    }

    public static void Test_MonsterKernel_ParticipantsWithoutPetsAndOnAnExtraTurn()
    {
        var log = new List<string>();
        var cs = NewCombat(45UL);
        var a = AddEnemy(cs, new ProbeDef(log));
        var b = AddEnemy(cs, new ProbeDef(log));
        b.CurrentHp = 0;
        var players = MonsterLifecycle.PlayerSideParticipants(cs);
        Assert.SequenceEqual(new CreatureState[] { cs.Player }, players);
        Assert.False(ReferenceEquals(players, cs.Allies), "a snapshot, not the live list");
        var enemies = MonsterLifecycle.EnemySideParticipants(cs);
        Assert.SequenceEqual(new CreatureState[] { a, b }, enemies, "every listed enemy, dead ones included");
        cs.Monsters.Remove(a);
        Assert.SequenceEqual(new CreatureState[] { a, b }, enemies, "a later removal does not change the snapshot");
        Assert.False(ReferenceEquals(enemies, MonsterLifecycle.EnemySideParticipants(cs)), "a fresh array per call");

        cs.PlayersTakingExtraTurn.Add(cs.Player.CombatId);
        Assert.SequenceEqual(new CreatureState[] { cs.Player }, MonsterLifecycle.PlayerSideParticipants(cs), "an extra turn starts only the players taking it");
        cs.PlayersTakingExtraTurn[0] = 77;
        Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.PlayerSideParticipants(cs));
        cs.Monsters.Clear();
        Assert.Equal(0, MonsterLifecycle.EnemySideParticipants(cs).Count);
    }

    public static void Test_MonsterKernel_ParticipantsIncludePetsInInsertionOrder()
    {
        W1Pending.Run(nameof(Test_MonsterKernel_ParticipantsIncludePetsInInsertionOrder), "state", () =>
        {
            var log = new List<string>();
            var cs = NewCombat(46UL);
            var enemy = AddEnemy(cs, new ProbeDef(log));
            var first = MonsterSpawner.SpawnPet(cs, new ProbeDef(log), cs.Player);
            var second = MonsterSpawner.SpawnPet(cs, new ProbeDef(log), cs.Player);
            Assert.SequenceEqual(new CreatureState[] { cs.Player, first, second }, MonsterLifecycle.PlayerSideParticipants(cs), "turn start covers the player and its pets");
            Assert.SequenceEqual(new CreatureState[] { enemy }, MonsterLifecycle.EnemySideParticipants(cs));
            first.CurrentHp = 0;
            Assert.SequenceEqual(new CreatureState[] { cs.Player, first, second }, MonsterLifecycle.PlayerSideParticipants(cs), "a dead pet stays in Allies");
            cs.PlayersTakingExtraTurn.Add(cs.Player.CombatId);
            Assert.SequenceEqual(new CreatureState[] { cs.Player }, MonsterLifecycle.PlayerSideParticipants(cs), "an extra turn excludes pets");
        });
    }

    public static void Test_MonsterKernel_SpawnAndMoveRollsDrawOnlyFromTheRunStreams()
    {
        var log = new List<string>();
        var cs = NewCombat(47UL);
        var def = new ProbeDef(log, 10, 14, randomGraph: true);
        var before = Counters(cs);
        var spawned = new List<MonsterState>();
        for (int i = 0; i < 3; i++)
        {
            var monster = MonsterSpawner.Spawn(cs, new MonsterSpawn(def));
            cs.Monsters.Add(monster);
            spawned.Add(monster);
        }
        AssertAdvanced(before, Counters(cs), (RunRngType.Niche, 3));

        before = Counters(cs);
        MonsterSpawner.SpawnForAdd(cs, def, CombatSide.Enemy, null);
        AssertAdvanced(before, Counters(cs), (RunRngType.Niche, 1), (RunRngType.MonsterAi, 1));

        before = Counters(cs);
        MonsterSpawner.CreateCreature(cs, def, CombatSide.Player, null);
        AssertAdvanced(before, Counters(cs));

        foreach (var monster in spawned)
        {
            MonsterLifecycle.SetUpForCombat(cs, monster);
        }
        before = Counters(cs);
        for (int round = 0; round < 5; round++)
        {
            foreach (var monster in spawned)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, monster);
                MonsterLifecycle.PerformMove(cs, monster);
            }
        }
        AssertAdvanced(before, Counters(cs), (RunRngType.MonsterAi, 15));

        foreach (var id in MonsterRegistry.AllIds)
        {
            var registered = MonsterRegistry.Get(id);
            Assert.False(registered.UsesPrivateRng, id + ": no v0.111.0 rule reads MonsterModel.Rng");
            AssertHoldsNoRng(registered.GetType(), typeof(MonsterDef));
        }
        foreach (var id in EncounterRegistry.AllIds)
        {
            AssertHoldsNoRng(EncounterRegistry.Get(id).GetType(), typeof(EncounterDef));
        }
        AssertHoldsNoRng(typeof(MonsterDef), typeof(object));
        AssertHoldsNoRng(typeof(MonsterListener), typeof(AbstractModel));
    }

    public static void Test_MonsterKernel_PrivateRngLivesInTheExtraStateAndIsClonedAndHashed()
    {
        var cs = NewCombat(48UL);
        var context = CombatRunContext.ForAct(2, 20, 3, 7);
        var monster = MonsterSpawner.Spawn(cs, new MonsterSpawn(new PrivateRngDef()), context);
        var extra = (PrivateRngProbeExtra)monster.Extra!;
        var rng = Assert.NotNull(extra.PrivateRng, "InitPrivateRng stores the Rng in the monster's extra state");
        Assert.Equal(context.CreateMonsterRng(cs.Rng.Seed, monster.CombatId).ToSnapshot(), rng.ToSnapshot());

        var clone = monster.DeepClone();
        var cloneRng = Assert.NotNull(((PrivateRngProbeExtra)clone.Extra!).PrivateRng);
        Assert.False(ReferenceEquals(rng, cloneRng), "the clone owns its Rng");
        Assert.Equal(rng.ToSnapshot(), cloneRng.ToSnapshot());
        Assert.Equal(StateHasher.Hash(monster), StateHasher.Hash(clone));
        cloneRng.NextInt(10);
        Assert.Equal(context.CreateMonsterRng(cs.Rng.Seed, monster.CombatId).ToSnapshot(), rng.ToSnapshot(), "drawing on the clone leaves the source alone");
        Assert.NotEqual(StateHasher.Hash(monster), StateHasher.Hash(clone), "the private Rng is hashed");

        Assert.Equal(0, cs.Run.Fidelity.Count, "an encounter spawn knows its run context");
        var added = MonsterSpawner.SpawnForAdd(cs, new PrivateRngDef(), CombatSide.Enemy, null);
        Assert.Equal(CombatRunContext.Default.CreateMonsterRng(cs.Rng.Seed, added.CombatId).ToSnapshot(), ((PrivateRngProbeExtra)added.Extra!).PrivateRng!.ToSnapshot());
        Assert.Equal(1, cs.Run.Fidelity.Count);
        Assert.Equal(new FidelityEntry(FidelityFlag.ApproximatedRule, MonsterSpawner.UnknownRunContextSite), cs.Run.Fidelity.Entries[0]);

        Assert.Throws<InvalidOperationException>(() => MonsterSpawner.Spawn(cs, new MonsterSpawn(new PrivateRngDef(withHolder: false))), "an opt-in def needs PrivateRngExtraState storage");
    }

    public static void Test_MonsterKernel_PerformMoveRecordsTheMoveAfterOnMovePerformed()
    {
        var log = new List<string>();
        var cs = NewCombat(49UL);
        int duringPerform = -1;
        var def = new ProbeDef(log, perform: (state, self, targets) =>
        {
            duringPerform = state.History.MonsterPerformedMoveEntries.Count;
            log.Add("perform:" + string.Join(",", targets.Select(t => t.CombatId)));
        });
        var monster = AddEnemy(cs, def);
        cs.CurrentSide = CombatSide.Enemy;
        cs.RoundNumber = 2;
        MonsterLifecycle.RollMove(cs, monster);
        MonsterLifecycle.PerformMove(cs, monster);
        Assert.Equal(0, duringPerform, "the entry is appended after the move");
        Assert.True(monster.Ai.PerformedFirstMove);
        Assert.False(monster.Ai.IsPerformingMove);
        var entries = cs.History.MonsterPerformedMoveEntries;
        Assert.Equal(1, entries.Count);
        var entry = entries[0];
        Assert.Equal(ProbeId, entry.MonsterId);
        Assert.Equal(WaitMoveId, entry.MoveId);
        Assert.SequenceEqual(new[] { cs.Player.CombatId }, entry.TargetIds!);
        Assert.Equal(monster.CombatId, entry.Stamp.ActorId);
        Assert.Equal(2, entry.Stamp.RoundNumber);
        Assert.Equal(CombatSide.Enemy, entry.Stamp.CurrentSide);
        Assert.SequenceEqual(new[] { "graph:1", "perform:0" }, log);
    }

    public static void Test_MonsterKernel_UnsetMoveIsMarkedBeforeItsPerformThrows()
    {
        var log = new List<string>();
        var cs = NewCombat(50UL);
        var monster = AddEnemy(cs, new ProbeDef(log));
        var thrown = Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.PerformMove(cs, monster));
        Assert.Equal("No move has been set for the monster", thrown.Message);
        Assert.True(monster.Ai.IsPerformingMove, "the exception leaves IsPerformingMove set, as in the game");
        Assert.True(monster.Ai.PerformedAtLeastOnce.Contains(MoveStateDef.UnsetMoveId), "MoveState.PerformMove marks the UNSET_MOVE state before its perform throws");
        Assert.Equal(0, cs.History.MonsterPerformedMoveEntries.Count);
    }

    public static void Test_MonsterKernel_RemoveCreatureRunsBeforeRemovedFromRoomThenResets()
    {
        var log = new List<string>();
        var cs = NewCombat(51UL);
        var def = new ProbeDef(log, randomGraph: true);
        var monster = AddEnemy(cs, def);
        MonsterLifecycle.RollMove(cs, monster);
        var nextMove = monster.Ai.NextMove;
        var clone = cs.DeepClone();
        log.Clear();
        MonsterLifecycle.RemoveCreature(cs, monster);
        Assert.SequenceEqual(new[] { "removed-from-room:1" }, log);
        Assert.True(monster.Ai.IsMachineReset);
        Assert.False(MonsterLifecycle.HasMoveStateMachine(monster));
        Assert.True(ReferenceEquals(nextMove, monster.Ai.NextMove), "NextMove is a model field and survives the reset");
        Assert.True(cs.Monsters.Contains(monster), "CombatManager.RemoveCreature does not touch the creature lists");
        Assert.NotEqual(StateHasher.Hash(clone), StateHasher.Hash(cs), "the reset is part of the canonical form");
        var resetClone = cs.DeepClone();
        Assert.True(resetClone.Monsters[0].Ai.IsMachineReset);
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(resetClone));

        var before = Counters(cs);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        AssertAdvanced(before, Counters(cs));
        Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.RollMove(cs, monster));
        Assert.Throws<InvalidOperationException>(() => monster.Ai.OnMovePerformed(nextMove!));
        Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.StunInternal(cs, monster, static (_, _, _) => { }, null), "the stun reads the reset machine's state log");

        var bare = MonsterSpawner.CreateCreature(cs, def, CombatSide.Enemy, null);
        MonsterLifecycle.RemoveCreature(cs, bare);
        Assert.False(bare.HasAi, "a creature without a machine only runs BeforeRemovedFromRoom");
    }

    public static void Test_MonsterKernel_DeathDuringItsOwnMoveSkipsOnMovePerformed()
    {
        var log = new List<string>();
        var cs = NewCombat(52UL);
        var def = new ProbeDef(log, perform: static (state, self, _) =>
        {
            self.CurrentHp = 0;
            MonsterLifecycle.RemoveCreature(state, self);
        });
        var dying = AddEnemy(cs, def);
        var survivor = AddEnemy(cs, new ProbeDef(log));
        cs.CurrentSide = CombatSide.Enemy;
        MonsterLifecycle.RollMove(cs, dying);
        log.Clear();
        MonsterLifecycle.PerformMove(cs, dying);
        Assert.SequenceEqual(new[] { "removed-from-room:1" }, log);
        Assert.True(dying.Ai.IsMachineReset);
        Assert.False(dying.Ai.PerformedFirstMove, "MoveStateMachine?.OnMovePerformed is skipped once the machine is gone");
        Assert.False(dying.Ai.IsPerformingMove);
        Assert.Equal(1, cs.History.MonsterPerformedMoveEntries.Count, "the history entry is still recorded");
        Assert.SequenceEqual(new[] { survivor }, cs.Monsters, "the dead monster leaves the enemies at the end of its move");

        var kept = NewCombat(53UL);
        var keeper = new LogPower(log, "K") { KeepDeadCreatures = true };
        keeper.ApplyInternal(kept.Player, 1m);
        var listed = AddEnemy(kept, def);
        kept.CurrentSide = CombatSide.Enemy;
        MonsterLifecycle.RollMove(kept, listed);
        MonsterLifecycle.PerformMove(kept, listed);
        Assert.SequenceEqual(new[] { listed }, kept.Monsters, "ShouldCreatureBeRemovedFromCombatAfterDeath false keeps the dead monster listed");
    }

    public static void Test_MonsterKernel_KillDuringItsOwnMoveRunsBeforeRemovedFromRoom()
    {
        W1Pending.Run(nameof(Test_MonsterKernel_KillDuringItsOwnMoveRunsBeforeRemovedFromRoom), "pipeline", () =>
        {
            var log = new List<string>();
            var cs = NewCombat(54UL);
            var def = new ProbeDef(log, perform: static (state, self, _) => CreatureCmd.Kill(state, self));
            var dying = AddEnemy(cs, def);
            var survivor = AddEnemy(cs, new ProbeDef(log));
            cs.CurrentSide = CombatSide.Enemy;
            MonsterLifecycle.RollMove(cs, dying);
            log.Clear();
            MonsterLifecycle.PerformMove(cs, dying);
            if (!log.Contains("removed-from-room:1"))
            {
                throw new NotImplementedException("pipeline: CreatureCmd.Kill does not yet call MonsterLifecycle.RemoveCreature (CombatManager.RemoveCreature) for an enemy it removes, also while that enemy performs its move (Commands/CreatureCmd.cs:2812-2822)");
            }
            Assert.True(dying.Ai.IsMachineReset);
            Assert.False(dying.Ai.PerformedFirstMove);
            Assert.SequenceEqual(new[] { survivor }, cs.Monsters);
        });
    }

    public static void Test_MonsterKernel_NibbitsWeakListenersCloneAndRecordMoves()
    {
        var sim = CombatSim.Create(5UL, StartingDecks.Ironclad, "NIBBITS_WEAK");
        var nibbit = sim.State.Monsters[0];
        Assert.True(ReferenceEquals(nibbit, Assert.NotNull(nibbit.Listener as MonsterListener).Owner), "the loop's SetUpForCombat installs the listener");
        for (int turn = 0; turn < 3 && !sim.IsTerminal; turn++)
        {
            var clone = sim.Clone();
            Assert.Equal(sim.StateHash(), clone.StateHash(), $"turn {turn}");
            var cloned = clone.State.Monsters[0];
            Assert.True(ReferenceEquals(cloned, ((MonsterListener)cloned.Listener!).Owner), $"turn {turn}: the clone's listener routes to the clone");
            Assert.False(ReferenceEquals(cloned.Listener, sim.State.Monsters[0].Listener));
            sim.Step(GameAction.EndTurn);
            clone.Step(GameAction.EndTurn);
            Assert.Equal(sim.StateHash(), clone.StateHash(), $"turn {turn}: lockstep after the enemy turn");
        }
        var moves = sim.State.History.MonsterPerformedMoveEntries;
        Assert.SequenceEqual(new[] { Nibbit.ButtMoveId, Nibbit.SliceMoveId, Nibbit.HissMoveId }.Take(moves.Count), moves.Select(e => e.MoveId), "one entry per enemy turn, in the alone cycle");
        Assert.True(moves.Count > 0);
        Assert.True(moves.All(e => e.MonsterId == Nibbit.MonsterIdEntry && e.Stamp.CurrentSide == CombatSide.Enemy && e.TargetIds!.SequenceEqual(new[] { sim.State.Player.CombatId })));
    }

    public static void Test_MonsterKernel_CanonicalTagsAreUniqueAndLiveInOneFile()
    {
        var tags = typeof(MonsterCanonicalTags).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(ushort))
            .Select(f => (Name: f.Name, Value: (ushort)f.GetRawConstantValue()!))
            .ToArray();
        Assert.True(tags.All(t => t.Value >= 0x4D00 && t.Value <= 0x4DFF), "monsters own 0x4Dxx");
        Assert.Equal(tags.Length, tags.Select(t => t.Value).Distinct().Count(), "no duplicate tag");
        Assert.Equal((ushort)0x4D01, MonsterCanonicalTags.AiRuntime);
        Assert.Equal((ushort)0x4D02, MonsterCanonicalTags.AiStateRef);
        Assert.Equal((ushort)0x4D10, MonsterCanonicalTags.NibbitExtra);
        Assert.Equal((ushort)0x4D31, MonsterCanonicalTags.ToadpoleExtra);
        Assert.Equal((ushort)0x4D41, MonsterCanonicalTags.CorpseSlugExtra);
        var strays = typeof(MonsterDef).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(MonsterDef).Namespace && t != typeof(MonsterCanonicalTags))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f.IsLiteral && f.FieldType == typeof(ushort))
            .Select(f => f.DeclaringType!.Name + "." + f.Name)
            .ToArray();
        Assert.Equal(0, strays.Length, "canonical tags declared outside MonsterCanonicalTags: " + string.Join(", ", strays));
    }

    private static void RequireMonsterListenerSlot(List<AbstractModel> buffer, MonsterState monster)
    {
        if (!buffer.Contains(monster.Listener!))
        {
            throw new NotImplementedException("hooks: the combat listener list does not yet yield MonsterState.Listener after its creature's powers (kernel-gaps.json listener_order.combat_list; Combat/CombatState.cs:864-867)");
        }
    }

    private static CombatState NewCombat(ulong seed = 7UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, TestEncounterId, 0, new RunRngSet(seed));
        cs.IsInProgress = true;
        cs.CurrentSide = CombatSide.Player;
        return cs;
    }

    private static MonsterState AddEnemy(CombatState cs, MonsterDef def, string? slotName = null)
    {
        var monster = MonsterSpawner.CreateCreature(cs, def, CombatSide.Enemy, slotName);
        cs.AddMonster(monster);
        MonsterLifecycle.SetUpForCombat(cs, monster);
        return monster;
    }

    private static int[] Counters(CombatState cs)
    {
        var types = Enum.GetValues<RunRngType>();
        var counters = new int[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            counters[i] = cs.Rng.Get(types[i]).Counter;
        }
        return counters;
    }

    private static void AssertAdvanced(int[] before, int[] after, params (RunRngType Stream, int Draws)[] expected)
    {
        var types = Enum.GetValues<RunRngType>();
        for (int i = 0; i < types.Length; i++)
        {
            int draws = 0;
            foreach (var (stream, count) in expected)
            {
                if (stream == types[i])
                {
                    draws = count;
                }
            }
            Assert.Equal(draws, after[i] - before[i], $"{types[i]} counter delta");
        }
    }

    private static void AssertHoldsNoRng(Type type, Type stopAt)
    {
        for (var t = type; t != null && t != stopAt; t = t.BaseType)
        {
            foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var ft = field.FieldType;
                bool rng = typeof(Rng).IsAssignableFrom(ft) || typeof(RunRngSet).IsAssignableFrom(ft) || typeof(MegaRandom).IsAssignableFrom(ft) || typeof(System.Random).IsAssignableFrom(ft);
                Assert.False(rng, $"{t.Name}.{field.Name} holds randomness; monster rolls must come from cs.Rng");
            }
        }
    }

    private static byte[] Canonical(ICanonical value)
    {
        var w = new CanonicalWriter(1024);
        value.WriteCanonical(w);
        return w.ToArray();
    }

    private static void NoOp(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private sealed class ProbeDef : MonsterDef
    {
        private readonly List<string> _log;
        private readonly int _min;
        private readonly int _max;
        private readonly bool _randomGraph;
        private readonly MovePerform _perform;

        public ProbeDef(List<string> log, int min = 10, int max = 12, bool randomGraph = false, MovePerform? perform = null)
            : base(ProbeId)
        {
            _log = log;
            _min = min;
            _max = max;
            _randomGraph = randomGraph;
            _perform = perform ?? NoOp;
        }

        public override int MinInitialHp(CombatState cs) => _min;

        public override int MaxInitialHp(CombatState cs) => _max;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            _log.Add("graph:" + self.CombatId);
            if (!_randomGraph)
            {
                var wait = new MoveStateDef(WaitMoveId, _perform, Intents.Buff) { FollowUpStateId = WaitMoveId };
                return new MonsterMoveStateMachineDef(new AiStateDef[] { wait }, wait);
            }
            var a = new MoveStateDef(MoveA, _perform, Intents.Buff) { FollowUpStateId = BranchId };
            var b = new MoveStateDef(MoveB, _perform, Intents.Buff) { FollowUpStateId = BranchId };
            var branch = new RandomBranchStateDef(BranchId);
            branch.AddBranch(a, MoveRepeatType.CanRepeatForever);
            branch.AddBranch(b, MoveRepeatType.CanRepeatForever);
            return new MonsterMoveStateMachineDef(new AiStateDef[] { a, b, branch }, branch);
        }

        public override void AfterAddedToRoom(CombatState cs, MonsterState self) => _log.Add("added:" + self.CombatId);

        public override void BeforeRemovedFromRoom(CombatState cs, MonsterState self) => _log.Add("removed-from-room:" + self.CombatId);

        public override void AfterCardChangedPilesLate(RunState rs, MonsterState self, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
            => _log.Add($"piles-late:{self.CombatId}:{card.InstanceId}:{oldPileType}");

        public override void AfterCardGeneratedForCombat(CombatState cs, MonsterState self, CardInstance card, PlayerState? creator)
            => _log.Add($"generated:{self.CombatId}:{card.InstanceId}:{creator?.CombatId}");

        public override void AfterCurrentHpChanged(RunState rs, MonsterState self, CreatureState creature, decimal delta)
            => _log.Add($"hp-changed:{self.CombatId}:{creature.CombatId}:{delta}");

        public override void AfterDamageReceived(RunState rs, MonsterState self, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
            => _log.Add($"damage-received:{self.CombatId}:{target.CombatId}:{result.UnblockedDamage}:{props}:{dealer?.CombatId}:{cardSource?.InstanceId}");

        public override void BeforeDeath(RunState rs, MonsterState self, CreatureState creature)
            => _log.Add($"before-death:{self.CombatId}:{creature.CombatId}");

        public override void AfterDeath(RunState rs, MonsterState self, CreatureState creature, bool wasRemovalPrevented)
            => _log.Add($"after-death:{self.CombatId}:{creature.CombatId}:{wasRemovalPrevented}");
    }

    private sealed class PrivateRngProbeExtra : PrivateRngExtraState
    {
        public int Marker { get; set; }

        protected override PrivateRngExtraState CloneFields() => new PrivateRngProbeExtra { Marker = Marker };

        protected override void WriteFieldsCanonical(CanonicalWriter w) => w.WriteInt32(Marker);
    }

    private sealed class PrivateRngDef : MonsterDef
    {
        private readonly bool _withHolder;

        public PrivateRngDef(bool withHolder = true)
            : base("MONSTER_KERNEL_PRIVATE_RNG")
        {
            _withHolder = withHolder;
        }

        public override bool UsesPrivateRng => true;

        public override MonsterExtraState? CreateExtraState() => _withHolder ? new PrivateRngProbeExtra() : null;

        public override int MinInitialHp(CombatState cs) => 5;

        public override int MaxInitialHp(CombatState cs) => 9;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var wait = new MoveStateDef(WaitMoveId, NoOp) { FollowUpStateId = WaitMoveId };
            return new MonsterMoveStateMachineDef(new AiStateDef[] { wait }, wait);
        }
    }

    private sealed class SlottedEncounter : EncounterDef
    {
        private readonly string[] _slots;

        public SlottedEncounter(params string[] slots)
            : base(TestEncounterId)
        {
            _slots = slots;
        }

        public override IReadOnlyList<string> Slots => _slots;

        public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => Array.Empty<MonsterSpawn>();
    }

    private sealed class LogPower : PowerModel
    {
        private readonly List<string> _log;
        private readonly string _tag;

        public LogPower(List<string> log, string tag)
        {
            _log = log;
            _tag = tag;
        }

        public bool KeepDeadCreatures { get; set; }

        public override string Id => "MONSTER_KERNEL_LOG_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature) => _log.Add($"{_tag}:creature-added:{creature.CombatId}");

        public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented) => _log.Add($"{_tag}:after-death:{creature.CombatId}");

        public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => !KeepDeadCreatures;
    }
}
