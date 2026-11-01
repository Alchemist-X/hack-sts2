using System.Diagnostics;
using System.Reflection;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Cards.Afflictions;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Tests;

public static class StateKernelTests
{
    private const string Encounter = "NIBBITS_WEAK";
    private const string Character = "IRONCLAD";
    private const int AfterCreatedReplayCount = 3;
    private const double HistoryCloneBudgetMicroseconds = 50.0;
    private const int HistoryCloneEntries = 500;

    private static readonly KernelCardDef StrikeDef = new("STATE_KERNEL_STRIKE", CardType.Attack);
    private static readonly KernelCardDef GuardDef = new("STATE_KERNEL_GUARD", CardType.Skill);
    private static readonly KernelCardDef SlimeDef = new("STATE_KERNEL_SLIME", CardType.Status);
    private static readonly KernelMonsterDef EnemyDef = new("STATE_KERNEL_ENEMY");
    private static readonly KernelMonsterDef PetDef = new("STATE_KERNEL_PET");
    private static readonly KernelMonsterDef OstyDef = new(PlayerState.OstyMonsterId);

    public static void Test_State_RunCreateStandaloneAppliesTightBeltOncePerPlayer()
    {
        for (int ascension = 0; ascension <= 10; ascension++)
        {
            var player = NewPlayer();
            Assert.Equal(PlayerState.DefaultPotionSlotCount, player.MaxPotionCount, $"A{ascension}: a new player has 3 slots");
            var run = RunState.CreateStandalone(player, new RunRngSet(7UL), ascension);
            int expected = ascension >= (int)AscensionLevel.TightBelt ? 2 : 3;
            Assert.Equal(expected, player.MaxPotionCount, $"A{ascension}: slots after the first CreateStandalone");
            Assert.True(player.AscensionEffectsApplied, $"A{ascension}: the flag is set at every ascension");
            Assert.True(ReferenceEquals(player, run.Players[0]), $"A{ascension}: the player is the run's player");
            Assert.Equal(ascension, run.Ascension);
            Assert.Null(run.Combat);
            RunState.CreateStandalone(player, new RunRngSet(7UL), ascension);
            Assert.Equal(expected, player.MaxPotionCount, $"A{ascension}: a second CreateStandalone leaves the slot count unchanged");
            var cs = new CombatState(player, Encounter, ascension, new RunRngSet(7UL));
            Assert.Equal(expected, cs.Player.MaxPotionCount, $"A{ascension}: a combat built on the same player keeps the slot count");
        }
        var plain = NewPlayer();
        var standalone = RunState.CreateStandalone(plain, new RunRngSet(1UL), 0);
        Assert.True(ReferenceEquals(UnlockProfile.All, standalone.Unlocks), "unlocks default to UnlockProfile.All");
        Assert.Equal(0, standalone.Modifiers.Count);
        Assert.Equal(0, standalone.Fidelity.Count);
        Assert.Equal(0, standalone.Choice.Selectors.Count);
        Assert.Throws<ArgumentNullException>(() => RunState.CreateStandalone(null!, new RunRngSet(1UL), 0));
    }

    public static void Test_State_RunCloneLinksEveryReachableObjectOnce()
    {
        var cs = BuildRichState(5UL);
        var clone = cs.DeepClone();
        Assert.True(ReferenceEquals(clone.Player, clone.Run.Players[0]), "clone.Player is clone.Run.Players[0]");
        Assert.True(ReferenceEquals(clone.Player, clone.Allies[0]), "clone.Allies[0] is clone.Player");
        Assert.True(ReferenceEquals(clone, clone.Run.Combat), "the clone is attached to its run");
        Assert.True(ReferenceEquals(clone.Rng, clone.Run.Rng), "clone.Rng is clone.Run.Rng");
        Assert.True(ReferenceEquals(clone.Choice, clone.Run.Choice), "clone.Choice is clone.Run.Choice");
        Assert.False(ReferenceEquals(clone.Player, cs.Player), "the player is cloned");
        Assert.False(ReferenceEquals(clone.Run, cs.Run), "the run is cloned");
        Assert.False(ReferenceEquals(clone.Rng, cs.Rng), "the rng set is cloned");
        Assert.False(ReferenceEquals(clone.Choice, cs.Choice), "the choice runtime is cloned");
        Assert.False(ReferenceEquals(clone.History, cs.History), "the history is cloned");
        Assert.False(ReferenceEquals(clone.Run.Fidelity, cs.Run.Fidelity), "the fidelity log is cloned");
        Assert.True(ReferenceEquals(clone.Run.Unlocks, cs.Run.Unlocks), "the unlock profile is shared");
        Assert.Equal(cs.Allies.Count, clone.Allies.Count);
        for (int i = 1; i < clone.Allies.Count; i++)
        {
            Assert.False(ReferenceEquals(clone.Allies[i], cs.Allies[i]), $"pet {i} is cloned");
            Assert.True(ReferenceEquals(clone.Allies[i], clone.GetCreatureById(clone.Allies[i].CombatId)), $"pet {i} resolves in the clone");
        }
        foreach (var relic in clone.Player.Relics)
        {
            Assert.True(ReferenceEquals(clone.Player, relic.Owner), $"relic {relic.Id} is rebound to the cloned player");
        }
        foreach (var potion in clone.Player.Potions)
        {
            Assert.True(ReferenceEquals(clone.Player, potion.Owner), $"potion {potion.Id} is rebound to the cloned player");
        }
        foreach (var pile in clone.Player.AllPiles)
        {
            foreach (var card in pile.Cards)
            {
                Assert.True(ReferenceEquals(clone.Player, card.Owner), $"pile card {card.Id}#{card.InstanceId} is rebound");
            }
        }
        foreach (var card in clone.Player.Deck)
        {
            Assert.True(ReferenceEquals(clone.Player, card.Owner), $"deck card {card.Id}#{card.InstanceId} is rebound");
        }
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone), "a standalone combat clone hashes like its source");

        var runClone = cs.Run.DeepClone();
        var attached = Assert.NotNull(runClone.Combat);
        Assert.True(ReferenceEquals(attached.Run, runClone), "the run clone's combat points back at it");
        Assert.True(ReferenceEquals(attached.Player, runClone.Players[0]), "the run clone's combat uses the run clone's player");
        Assert.True(ReferenceEquals(attached.Player, attached.Allies[0]), "the run clone's Allies[0] is its player");
        Assert.Equal(StateHasher.Hash(cs.Run), StateHasher.Hash(runClone), "a run clone hashes like its source");
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(attached), "the run clone's combat hashes like the source combat");
        AssertNoSharedMutable(cs, clone, "standalone clone");
        AssertNoSharedMutable(cs.Run, runClone, "run clone");
    }

    public static void Test_State_RunCanonicalWritesEachSharedObjectOnce()
    {
        var cs = BuildRichState(9UL);
        var combatBytes = StateHasher.CanonicalBytes(cs);
        var runBytes = StateHasher.CanonicalBytes(cs.Run);
        var run = cs.Run;
        run.Combat = null;
        var runFieldsBytes = StateHasher.CanonicalBytes(run);
        run.Combat = cs;
        int runFields = runFieldsBytes.Length - 1;
        Assert.Equal((byte)0, runFieldsBytes[runFields], "a run without a combat ends with an absent-combat byte");
        Assert.Equal(combatBytes.Length + 1, runBytes.Length, "the run form is the combat form plus one presence byte");
        Assert.True(runBytes.AsSpan(0, runFields).SequenceEqual(combatBytes.AsSpan(0, runFields)), "both forms start with the run fields");
        Assert.True(runFieldsBytes.AsSpan(0, runFields).SequenceEqual(combatBytes.AsSpan(0, runFields)), "the run fields do not depend on the attached combat");
        Assert.Equal((byte)1, runBytes[runFields], "the run form marks the attached combat");
        Assert.True(runBytes.AsSpan(runFields + 1).SequenceEqual(combatBytes.AsSpan(runFields)), "the combat fields follow, written once");
    }

    public static void Test_State_RunCombatAttachmentStaysWithItsRun()
    {
        var player = NewPlayer();
        var run = RunState.CreateStandalone(player, new RunRngSet(3UL), 0);
        var first = new CombatState(run, player, Encounter);
        Assert.True(ReferenceEquals(first, run.Combat), "the first combat attaches itself");
        var second = new CombatState(run, player, Encounter);
        Assert.True(ReferenceEquals(first, run.Combat), "a second combat on the same run stays detached");
        var other = new CombatState(NewPlayer(), Encounter, 0, new RunRngSet(3UL));
        Assert.Throws<InvalidOperationException>(() => run.Combat = other);
        Assert.True(ReferenceEquals(first, run.Combat), "a rejected attachment leaves the run unchanged");
        var detachedClone = second.DeepClone();
        Assert.Null(detachedClone.Run.Combat, "a detached combat's clone is not attached to the cloned run");
        Assert.True(ReferenceEquals(detachedClone.Player, detachedClone.Run.Players[0]), "the detached clone still links its player to the run");
        Assert.Equal(StateHasher.Hash(second), StateHasher.Hash(detachedClone));
        run.Combat = null;
        Assert.Null(run.Combat);
        run.Combat = second;
        Assert.True(ReferenceEquals(second, run.Combat), "a combat of this run can be attached after a detach");
    }

    public static void Test_State_AlliesStartWithThePlayerAndKeepPetsInInsertionOrder()
    {
        var cs = NewCombat();
        Assert.Equal(1, cs.Allies.Count);
        Assert.True(ReferenceEquals(cs.Player, cs.Allies[0]));
        var enemy = AddEnemy(cs, 20);
        var petA = AddPet(cs, PetDef, 9);
        var petB = AddPet(cs, OstyDef, 7);
        Assert.Equal(3, cs.Allies.Count);
        Assert.True(ReferenceEquals(petA, cs.Allies[1]) && ReferenceEquals(petB, cs.Allies[2]), "pets follow the player in insertion order");
        Assert.True(ReferenceEquals(petA, cs.GetCreatureById(petA.CombatId)), "GetCreatureById finds a pet");
        Assert.True(ReferenceEquals(enemy, cs.GetCreatureById(enemy.CombatId)), "GetCreatureById finds an enemy");
        Assert.Null(cs.GetCreatureById(PowerModel.NoCreature));
        Assert.Null(cs.GetCreatureById(999));
        var buffer = new List<CreatureState> { enemy };
        cs.GetCreatures(buffer);
        Assert.SequenceEqual(new CreatureState[] { cs.Player, petA, petB, enemy }, buffer, "GetCreatures walks allies, then enemies");
        Assert.True(ReferenceEquals(cs.Allies, cs.GetCreaturesOnSide(CombatSide.Player)), "the player side is the live ally list");
        Assert.True(ReferenceEquals(cs.Allies, cs.GetCreaturesOnSide(CombatSide.None)), "any side but Enemy is the ally list");
        Assert.True(ReferenceEquals(cs.Monsters, cs.GetCreaturesOnSide(CombatSide.Enemy)), "the enemy side is the live monster list");
        Assert.True(ReferenceEquals(cs.Allies, cs.GetTeammatesOf(petB)), "a pet's teammates are the allies");
        Assert.True(ReferenceEquals(cs.Monsters, cs.GetOpponentsOf(petB)), "a pet's opponents are the enemies");
        Assert.True(ReferenceEquals(cs.Allies, cs.GetOpponentsOf(enemy)), "an enemy's opponents are the allies");
        cs.CurrentSide = CombatSide.Player;
        Assert.True(ReferenceEquals(cs.Allies, cs.CreaturesOnCurrentSide));
        cs.CurrentSide = CombatSide.Enemy;
        Assert.True(ReferenceEquals(cs.Monsters, cs.CreaturesOnCurrentSide));
        var snapshot = new List<CreatureState>();
        cs.GetCreaturesOnSide(CombatSide.Player, snapshot);
        Assert.SequenceEqual(new CreatureState[] { cs.Player, petA, petB }, snapshot);
        Assert.True(cs.ContainsCreature(petA) && cs.ContainsCreature(enemy) && cs.ContainsCreature(cs.Player));
        Assert.False(cs.ContainsCreature(new MonsterState(77, EnemyDef, 5, 5, null, null)));
        var hittable = new List<MonsterState> { petA };
        cs.GetHittableEnemies(hittable);
        Assert.SequenceEqual(new[] { enemy }, hittable, "pets are never hittable enemies");
    }

    public static void Test_State_AddCreatureRoutesBySideAndRejectsDuplicates()
    {
        var cs = NewCombat();
        var playerSide = new MonsterState(cs.AllocateCreatureId(), PetDef, CombatSide.Player, 5, 5, null, null);
        cs.AddMonster(playerSide);
        Assert.Equal(0, cs.Monsters.Count, "AddMonster sends a player-side creature to the allies, as AddCreature does");
        Assert.True(ReferenceEquals(playerSide, cs.Allies[1]));
        Assert.Throws<InvalidOperationException>(() => cs.AddAlly(playerSide));
        Assert.Throws<InvalidOperationException>(() => cs.AddMonster(playerSide));
        Assert.Throws<InvalidOperationException>(() => cs.AddCreature(cs.Player));
        var enemy = new MonsterState(cs.AllocateCreatureId(), EnemyDef, 8, 8, null, null);
        Assert.Throws<ArgumentException>(() => cs.AddAlly(enemy));
        Assert.Equal(0, cs.Monsters.Count, "a rejected AddAlly adds nothing");
        cs.AddCreature(enemy);
        Assert.True(ReferenceEquals(enemy, cs.Monsters[0]), "AddCreature sends an enemy-side creature to the enemies");
        Assert.Throws<InvalidOperationException>(() => cs.AddCreature(enemy));
        var stranger = NewPlayer(5);
        stranger.Side = CombatSide.Enemy;
        Assert.Throws<ArgumentException>(() => cs.AddCreature(stranger));
        Assert.Throws<ArgumentNullException>(() => cs.AddCreature(null!));
    }

    public static void Test_State_RemoveCreatureSearchesEnemiesThenAllies()
    {
        var cs = NewCombat();
        var enemy = AddEnemy(cs, 10);
        var pet = AddPet(cs, PetDef, 4);
        cs.RemoveAlly(pet);
        Assert.Equal(1, cs.Allies.Count, "RemoveAlly drops the pet");
        Assert.False(cs.ContainsCreature(pet));
        Assert.Throws<InvalidOperationException>(() => cs.RemoveAlly(pet));
        cs.RemoveMonster(enemy);
        Assert.Equal(0, cs.Monsters.Count);
        Assert.Throws<InvalidOperationException>(() => cs.RemoveMonster(enemy));
        var other = AddPet(cs, PetDef, 4);
        cs.RemoveCreature(other);
        Assert.False(cs.ContainsCreature(other), "RemoveCreature finds a pet in the allies");
        Assert.Throws<InvalidOperationException>(() => cs.RemoveCreature(cs.Player));
        Assert.True(ReferenceEquals(cs.Player, cs.Allies[0]), "the player stays in Allies[0]");
    }

    public static void Test_State_SetEnemyIndexFollowsTheGameOrderRule()
    {
        var cs = NewCombat();
        var a = AddEnemy(cs, 10);
        var b = AddEnemy(cs, 11);
        var c = AddEnemy(cs, 12);
        cs.SetEnemyIndex(c, 0);
        Assert.SequenceEqual(new[] { c, a, b }, cs.Monsters, "index 0 moves the monster to the front");
        cs.SetEnemyIndex(c, 99);
        Assert.SequenceEqual(new[] { a, c, b }, cs.Monsters, "an index past the end lands at Count - 1 measured after the removal");
        Assert.Throws<ArgumentException>(() => cs.SetEnemyIndex(new MonsterState(55, EnemyDef, 3, 3, null, null), 0));
        var single = NewCombat();
        var only = AddEnemy(single, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => single.SetEnemyIndex(only, 0));
        var unknown = new CombatState(NewPlayer(), "STATE_KERNEL_UNREGISTERED", 0, new RunRngSet(1UL));
        var stray = AddEnemy(unknown, 4);
        Assert.Throws<InvalidOperationException>(() => unknown.SetEnemyIndex(stray, 0));
    }

    public static void Test_State_PetRegistrationFollowsPlayerCombatState()
    {
        var cs = NewCombat();
        var pet = new MonsterState(cs.AllocateCreatureId(), PetDef, CombatSide.Player, 6, 6, null, null);
        cs.AddAlly(pet);
        Assert.False(pet.IsPet);
        cs.Player.AddPetInternal(pet);
        Assert.True(pet.IsPet);
        Assert.Equal(cs.Player.CombatId, pet.PetOwnerId);
        Assert.SequenceEqual(new[] { pet.CombatId }, cs.Player.PetIds);
        cs.Player.AddPetInternal(pet);
        Assert.SequenceEqual(new[] { pet.CombatId }, cs.Player.PetIds, "a second AddPetInternal is ignored");
        var otherOwner = NewPlayer(40);
        Assert.Throws<InvalidOperationException>(() => otherOwner.AddPetInternal(pet));
        Assert.Equal(0, otherOwner.PetIds.Count, "a pet owned by someone else is not added");
        Assert.Throws<InvalidOperationException>(() => pet.SetPetOwner(cs.Player));
        Assert.Null(cs.Player.GetOsty(cs));
        Assert.False(cs.Player.IsOstyAlive(cs));
        Assert.True(cs.Player.IsOstyMissing(cs));
        var osty = AddPet(cs, OstyDef, 1);
        Assert.True(ReferenceEquals(osty, cs.Player.GetOsty(cs)));
        Assert.True(ReferenceEquals(pet, cs.Player.GetPet(cs, PetDef.Id)));
        Assert.True(cs.Player.IsOstyAlive(cs));
        osty.CurrentHp = 0;
        Assert.False(cs.Player.IsOstyAlive(cs));
        Assert.True(cs.Player.IsOstyMissing(cs));
        var stranger = new MonsterState(cs.AllocateCreatureId(), PetDef, CombatSide.Player, 3, 3, null, null);
        Assert.Throws<InvalidOperationException>(() => cs.Player.OnPetDied(cs, stranger));
    }

    public static void Test_State_InvokeDiedEventDropsOnlySubscribedRemovablePets()
    {
        var cs = NewCombat();
        var pet = AddPet(cs, PetDef, 5);
        var enemy = AddEnemy(cs, 5);
        pet.CurrentHp = 0;
        cs.InvokeDiedEvent(pet);
        Assert.Equal(0, cs.Player.PetIds.Count, "a dead pet leaves the pet list when the combat removes the dead");
        Assert.True(ReferenceEquals(pet, cs.Allies[1]), "the dead pet stays in Allies");
        cs.InvokeDiedEvent(pet);
        Assert.Equal(0, cs.Player.PetIds.Count, "an unsubscribed pet is ignored");
        cs.InvokeDiedEvent(enemy);
        cs.InvokeDiedEvent(cs.Player);

        var keep = NewCombat();
        AttachPower(new KeepDeadPower(), keep.Player, 1);
        var kept = AddPet(keep, PetDef, 5);
        kept.CurrentHp = 0;
        keep.InvokeDiedEvent(kept);
        Assert.SequenceEqual(new[] { kept.CombatId }, keep.Player.PetIds, "a listener that keeps the dead keeps the pet listed");
        keep.Player.OnPetDied(keep, kept);
        Assert.SequenceEqual(new[] { kept.CombatId }, keep.Player.PetIds);
    }

    public static void Test_State_CreateCardAllocatesAttachesAndRunsAfterCreated()
    {
        var cs = NewCombat();
        int next = cs.NextCardInstanceId;
        var card = cs.CreateCard(StrikeDef, cs.Player);
        Assert.Equal(next, card.InstanceId, "the id comes from the run allocator");
        Assert.Equal(next + 1, cs.NextCardInstanceId);
        Assert.Equal(next + 1, cs.Run.NextCardInstanceId, "the combat forwards to the run allocator");
        Assert.True(ReferenceEquals(cs.Player, card.Owner), "CreateCard sets the owner");
        Assert.Equal(AfterCreatedReplayCount, card.BaseReplayCount, "AfterCreated runs on the new card");
        Assert.Null(cs.Player.FindPileContaining(card), "CreateCard puts the card in no pile");
        var registered = cs.CreateCard("STRIKE_IRONCLAD", cs.Player);
        Assert.Equal("STRIKE_IRONCLAD", registered.Id);
        Assert.Equal(next + 1, registered.InstanceId);
        Assert.Throws<ArgumentNullException>(() => cs.CreateCard((CardDef)null!, cs.Player));
        Assert.Throws<ArgumentNullException>(() => cs.CreateCard(StrikeDef, null!));
        Assert.Equal(next + 2, cs.NextCardInstanceId, "rejected calls allocate nothing");
    }

    public static void Test_State_CloneCardTakesANewIdAndNoDeckVersion()
    {
        var cs = NewCombat();
        var deckCard = cs.CreateCard(StrikeDef, cs.Player);
        var combatCopy = deckCard.CloneForCombat(cs.AllocateCardInstanceId());
        var clone = cs.CloneCard(combatCopy);
        Assert.NotEqual(combatCopy.InstanceId, clone.InstanceId);
        Assert.Equal(cs.NextCardInstanceId - 1, clone.InstanceId);
        Assert.Equal(CardInstance.NoDeckVersion, clone.DeckVersionId, "CloneCard leaves the deck version unset (AfterCloned)");
        Assert.Equal(deckCard.InstanceId, combatCopy.DeckVersionId);
        Assert.True(ReferenceEquals(cs.Player, clone.Owner), "the clone keeps the owner");
        Assert.False(ReferenceEquals(combatCopy.Vars, clone.Vars), "the clone has its own vars");
    }

    public static void Test_State_PlayerPersistentDefaultsFollowTheGame()
    {
        var player = NewPlayer();
        Assert.Equal(3, PlayerState.DefaultPotionSlotCount);
        Assert.Equal(3, player.MaxPotionCount);
        Assert.Equal(3, player.PotionSlots.Length);
        Assert.True(player.HasOpenPotionSlot);
        Assert.Equal(0, player.Gold);
        Assert.Equal(0, player.BaseOrbSlotCount);
        Assert.True(player.CanUseOrRemovePotions);
        Assert.False(player.AscensionEffectsApplied);
        Assert.Equal(0, player.Relics.Count);
        Assert.Equal(0, player.Deck.Count);
        Assert.Equal(0, player.PetIds.Count);
        Assert.Equal(0, player.OrbQueue.Capacity);
        Assert.Equal(0, player.Potions.Count());
    }

    public static void Test_State_PotionSlotGrowthAppendsEmptySlots()
    {
        var player = NewPlayer();
        var a = new KernelPotion("STATE_KERNEL_POTION_A");
        player.AddPotionInternal(a, 1);
        player.AddToMaxPotionCount(2);
        Assert.Equal(5, player.MaxPotionCount);
        Assert.True(ReferenceEquals(a, player.PotionSlots[1]), "existing potions keep their slot");
        Assert.Null(player.PotionSlots[3]);
        Assert.Null(player.PotionSlots[4]);
        player.AddToMaxPotionCount(0);
        Assert.Equal(5, player.MaxPotionCount, "a zero change keeps the slots");
    }

    public static void Test_State_PotionSlotShrinkMovesOrDiscardsLikeTheGame()
    {
        var p = Potions(5);
        var player = WithSlots(p[0], p[1], null, p[3]);
        player.SubtractFromMaxPotionCount(2);
        Assert.SequenceEqual(new PotionModel?[] { p[0], p[1] }, player.PotionSlots, "the open slot 2 is not below the new count, so slot 3 is discarded");
        Assert.True(ReferenceEquals(player, p[3].Owner), "a discarded potion keeps its owner");
        Assert.False(p[3].HasBeenRemovedFromState, "the internal discard does not flag the potion");

        p = Potions(5);
        player = WithSlots(null, p[1], p[2]);
        player.SubtractFromMaxPotionCount(1);
        Assert.SequenceEqual(new PotionModel?[] { p[2], p[1] }, player.PotionSlots, "the last potion moves into the first open slot");

        p = Potions(5);
        player = WithSlots(p[0], null, null, p[3], p[4]);
        player.SubtractFromMaxPotionCount(3);
        Assert.SequenceEqual(new PotionModel?[] { p[0], p[4] }, player.PotionSlots, "slot 4 moves to slot 1, slot 3 finds only slot 2 open and is discarded");

        p = Potions(5);
        player = WithSlots(p[0], p[1], p[2]);
        var before = player.PotionSlots;
        Assert.Throws<ArgumentOutOfRangeException>(() => player.SubtractFromMaxPotionCount(1));
        Assert.True(ReferenceEquals(before, player.PotionSlots), "a full shrink throws like the game's write to slot -1 and keeps the slots");
        Assert.SequenceEqual(new PotionModel?[] { p[0], p[1], p[2] }, player.PotionSlots);

        player = WithSlots(null, null, null);
        player.SubtractFromMaxPotionCount(3);
        Assert.Equal(0, player.MaxPotionCount, "empty slots shrink to zero");
        Assert.False(player.HasOpenPotionSlot, "no slot is no open slot");
        Assert.Throws<ArgumentOutOfRangeException>(() => player.SubtractFromMaxPotionCount(1));
    }

    public static void Test_State_AddPotionInternalFillsFirstEmptyOrExplicitSlot()
    {
        var player = NewPlayer();
        var p = Potions(5);
        var first = player.AddPotionInternal(p[0]);
        Assert.True(first.Success);
        Assert.Equal(PotionProcureFailureReason.None, first.FailureReason);
        Assert.True(ReferenceEquals(p[0], first.Potion));
        Assert.True(ReferenceEquals(p[0], player.PotionSlots[0]), "slot -1 means the first empty slot");
        Assert.True(ReferenceEquals(player, p[0].Owner), "success sets the owner");
        var explicitSlot = player.AddPotionInternal(p[1], 2);
        Assert.True(explicitSlot.Success);
        Assert.True(ReferenceEquals(p[1], player.PotionSlots[2]), "an explicit slot index is honoured");
        var occupied = player.AddPotionInternal(p[2], 2);
        Assert.False(occupied.Success);
        Assert.Equal(PotionProcureFailureReason.TooFull, occupied.FailureReason, "an occupied slot fails as TooFull");
        Assert.False(p[2].HasOwner, "a failed add leaves the potion unowned");
        Assert.True(ReferenceEquals(p[1], player.PotionSlots[2]));
        var filler = player.AddPotionInternal(p[3]);
        Assert.True(filler.Success);
        Assert.True(ReferenceEquals(p[3], player.PotionSlots[1]));
        Assert.False(player.HasOpenPotionSlot);
        var full = player.AddPotionInternal(p[4]);
        Assert.False(full.Success);
        Assert.Equal(PotionProcureFailureReason.TooFull, full.FailureReason, "no empty slot fails as TooFull");
        Assert.False(p[4].HasOwner);
        Assert.Throws<IndexOutOfRangeException>(() => player.AddPotionInternal(p[4], 3));
        var otherPlayer = NewPlayer(9);
        Assert.Throws<InvalidOperationException>(() => otherPlayer.AddPotionInternal(p[0]));
        Assert.Null(otherPlayer.PotionSlots[0], "a potion of another player is rejected before the slot write");
        Assert.Equal(2, player.GetPotionSlotIndex(p[1]));
        Assert.Equal(-1, player.GetPotionSlotIndex(p[4]));
        Assert.True(ReferenceEquals(p[3], player.GetPotionAtSlotIndex(1)));
        Assert.Throws<IndexOutOfRangeException>(() => player.GetPotionAtSlotIndex(3));
        Assert.Throws<IndexOutOfRangeException>(() => player.GetPotionAtSlotIndex(-1));
        Assert.SequenceEqual(new PotionModel[] { p[0], p[3], p[1] }, player.Potions, "Potions lists the occupied slots in slot order");
    }

    public static void Test_State_RemovePotionInternalsNullTheSlot()
    {
        var p = Potions(3);
        var player = WithSlots(p[0], p[1], p[2]);
        player.DiscardPotionInternal(p[1]);
        Assert.Null(player.PotionSlots[1]);
        Assert.False(p[1].HasBeenRemovedFromState, "the internal removal does not flag the potion (its callers do)");
        player.RemoveUsedPotionInternal(p[2]);
        Assert.Null(player.PotionSlots[2]);
        Assert.Throws<InvalidOperationException>(() => player.DiscardPotionInternal(p[1]));
        Assert.Throws<InvalidOperationException>(() => player.RemoveUsedPotionInternal(new KernelPotion("STATE_KERNEL_POTION_STRAY")));
        Assert.True(ReferenceEquals(p[0], player.PotionSlots[0]));
    }

    public static void Test_State_RelicInternalsFollowThePlayerMethods()
    {
        var player = NewPlayer();
        var a = new KernelRelic("STATE_KERNEL_RELIC_A");
        var b = new KernelRelic("STATE_KERNEL_RELIC_B");
        var c = new KernelRelic("STATE_KERNEL_RELIC_C");
        player.AddRelicInternal(a);
        player.AddRelicInternal(b);
        player.AddRelicInternal(c, 1);
        Assert.SequenceEqual(new RelicModel[] { a, c, b }, player.Relics, "index -1 appends, any other index inserts");
        Assert.True(ReferenceEquals(player, c.Owner));
        Assert.Throws<ArgumentOutOfRangeException>(() => player.AddRelicInternal(new KernelRelic("STATE_KERNEL_RELIC_D"), -2));
        var other = NewPlayer(3);
        Assert.Throws<InvalidOperationException>(() => other.AddRelicInternal(a));
        Assert.Equal(0, other.Relics.Count);
        Assert.True(ReferenceEquals(c, player.GetRelic("STATE_KERNEL_RELIC_C")));
        Assert.True(player.HasRelic("STATE_KERNEL_RELIC_A"));
        Assert.False(player.HasRelic("STATE_KERNEL_RELIC_Z"));
        Assert.True(ReferenceEquals(a, player.GetRelic<KernelRelic>()), "GetRelic<T> returns the first relic of the type");
        Assert.Null(player.GetRelic<KernelWaxRelic>());

        player.RemoveRelicInternal(c);
        Assert.SequenceEqual(new RelicModel[] { a, b }, player.Relics);
        Assert.True(c.HasBeenRemovedFromState, "removal marks the relic as removed from the state");
        Assert.True(ReferenceEquals(player, c.Owner), "a removed relic keeps its owner, as in the game");
        Assert.Throws<InvalidOperationException>(() => player.RemoveRelicInternal(c));

        var wax = new KernelWaxRelic();
        Assert.Throws<InvalidOperationException>(() => player.MeltRelicInternal(a));
        Assert.Throws<InvalidOperationException>(() => player.MeltRelicInternal(wax));
        Assert.False(wax.IsMelted, "melting an unowned wax relic fails before any change");
        player.AddRelicInternal(wax);
        player.MeltRelicInternal(wax);
        Assert.True(wax.IsMelted);
        Assert.Equal(RelicStatus.Disabled, wax.Status);
        Assert.Throws<InvalidOperationException>(() => player.MeltRelicInternal(wax));
        Assert.True(ReferenceEquals(wax, player.GetRelic("STATE_KERNEL_WAX_RELIC")), "GetRelic does not skip melted relics");
        Assert.True(ReferenceEquals(wax, player.GetRelic<KernelWaxRelic>()));
        Assert.True(player.HasRelic("STATE_KERNEL_WAX_RELIC"));
        var meltedAlready = new KernelWaxRelic { IsMelted = true };
        Assert.Throws<InvalidOperationException>(() => player.MeltRelicInternal(meltedAlready));
    }

    public static void Test_State_ResetCombatStateRebuildsTheCombatHalf()
    {
        W1Pending.Run(nameof(Test_State_ResetCombatStateRebuildsTheCombatHalf), "character-mechanics", () =>
        {
            var cs = BuildRichState(13UL);
            var player = cs.Player;
            player.BaseOrbSlotCount = 3;
            player.Energy = 2;
            player.Stars = 4;
            player.TurnNumber = 6;
            player.Phase = PlayerTurnPhase.Play;
            int gold = player.Gold;
            int relics = player.Relics.Count;
            int deck = player.Deck.Count;
            int historyCount = cs.History.Count;
            player.ResetCombatState();
            foreach (var pile in player.AllPiles)
            {
                Assert.Equal(0, pile.Count, $"{pile.Type} is empty after the reset");
            }
            Assert.Equal(1, player.TurnNumber);
            Assert.Equal(PlayerTurnPhase.None, player.Phase);
            Assert.Equal(0, player.Energy);
            Assert.Equal(0, player.Stars);
            Assert.Equal(0, player.PetIds.Count);
            Assert.Equal(0, player.OrbQueue.Orbs.Count);
            Assert.Equal(3, player.OrbQueue.Capacity, "the orb queue starts at the base orb slot count");
            Assert.Equal(gold, player.Gold);
            Assert.Equal(relics, player.Relics.Count);
            Assert.Equal(deck, player.Deck.Count);
            Assert.Equal(historyCount, cs.History.Count, "the reset writes no history");
        });
    }

    public static void Test_State_CombatStateAfterCombatEndClearsPilesAndPets()
    {
        var cs = BuildRichState(17UL);
        var player = cs.Player;
        int deck = player.Deck.Count;
        int turn = player.TurnNumber;
        player.CombatStateAfterCombatEnd();
        foreach (var pile in player.AllPiles)
        {
            Assert.Equal(0, pile.Count, $"{pile.Type} is empty after the combat");
        }
        Assert.Equal(0, player.PetIds.Count);
        Assert.Equal(deck, player.Deck.Count, "the deck survives the combat");
        Assert.Equal(turn, player.TurnNumber, "only the piles and pets are cleared");
    }

    public static void Test_State_HistoryAppendsStampEveryEntryType()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var enemy = AddEnemy(cs, 30);
        var pet = AddPet(cs, PetDef, 6);
        cs.RoundNumber = 3;
        cs.CurrentSide = CombatSide.Player;
        player.TurnNumber = 3;
        var history = cs.History;
        var strike = AddCard(cs, player.Hand, StrikeDef);
        var play = NewPlay(cs, strike, enemy, new ResourceInfo(1, 1, 0, 0));
        play.ResultPile = PileType.Discard;
        var block = new DamageResult(player, ValueProp.Move) { BlockedDamage = 3, UnblockedDamage = 4, WasBlockBroken = true };
        var hit = new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 6, OverkillDamage = 1, WasTargetKilled = true };
        var power = new KernelPower();
        AttachPower(power, enemy, 2);
        var orb = new KernelOrb();
        orb.AttachTo(player);
        var potion = new KernelPotion("STATE_KERNEL_POTION_HISTORY");
        player.AddPotionInternal(potion);
        var affliction = new KernelAffliction();
        var move = new MoveStateDef("STATE_KERNEL_MOVE", static (_, _, _) => { });

        history.AppendCardPlayStarted(cs, play);
        history.AppendBlockGained(cs, player, 5, ValueProp.Move, play);
        history.AppendDamageReceived(cs, enemy, player, hit, strike);
        history.AppendCreatureAttacked(cs, player, new[] { hit });
        history.AppendCardPlayFinished(cs, play);
        history.AppendEnergySpent(cs, 1, player);
        history.AppendCardDrawn(cs, strike, true);
        history.AppendCardDiscarded(cs, strike);
        history.AppendCardExhausted(cs, strike);
        history.AppendCardGenerated(cs, strike, player);
        history.AppendCardAfflicted(cs, strike, affliction);
        cs.CurrentSide = CombatSide.Enemy;
        history.AppendMonsterPerformedMove(cs, enemy, move, new CreatureState[] { player });
        history.AppendDamageReceived(cs, player, enemy, block, null);
        history.AppendOrbChanneled(cs, orb);
        history.AppendPotionUsed(cs, potion, enemy);
        history.AppendPowerReceived(cs, power, 2.50m, player);
        history.AppendStarsModified(cs, -2, player);
        history.AppendSummoned(cs, 4, player);

        Assert.Equal(18, history.Count);
        var ordered = new List<IHistoryEntry>();
        history.GetEntriesInOrder(ordered);
        Assert.Equal(18, ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            var stamp = ordered[i].Stamp;
            Assert.Equal(i, stamp.Sequence, $"entry {i} carries its global sequence");
            Assert.Equal(3, stamp.RoundNumber, $"entry {i} round");
            Assert.Equal(3, stamp.PlayerTurnNumber, $"entry {i} player turn");
            Assert.Equal(i < 11 ? CombatSide.Player : CombatSide.Enemy, stamp.CurrentSide, $"entry {i} side");
        }

        var started = history.CardPlaysStarted[0];
        Assert.Equal(player.CombatId, started.Stamp.ActorId, "a card entry's actor is the card owner");
        Assert.Equal(play.PlayId, started.CardPlay.PlayId);
        Assert.Equal(strike.InstanceId, started.CardPlay.CardInstanceId);
        Assert.Equal(StrikeDef.Id, started.CardPlay.CardId);
        Assert.Equal(player.CombatId, started.CardPlay.PlayerId);
        Assert.Equal(enemy.CombatId, started.CardPlay.TargetId);
        Assert.Equal(PileType.Discard, started.CardPlay.ResultPile);
        Assert.Equal(new ResourceInfo(1, 1, 0, 0), started.CardPlay.Resources);
        Assert.Equal(CardType.Attack, started.CardPlay.CardType);
        Assert.False(started.CardPlay.IsDupe);
        Assert.True(started.CardPlay.IsFirstInSeries && started.CardPlay.IsLastInSeries);

        var gained = history.BlockGainedEntries[0];
        Assert.Equal(player.CombatId, gained.ReceiverId, "block gained: the actor is the receiver");
        Assert.Equal(5, gained.Amount);
        Assert.Equal(ValueProp.Move, gained.Props);
        Assert.True(gained.CardPlay.HasValue && gained.CardPlay.Value.PlayId == play.PlayId);

        var dealt = history.DamageReceivedEntries[0];
        Assert.Equal(enemy.CombatId, dealt.ReceiverId, "damage received: the actor is the receiver");
        Assert.Equal(player.CombatId, dealt.DealerId);
        Assert.Equal(CombatSide.Player, dealt.DealerSide, "the dealer's side is snapshotted");
        Assert.Equal(strike.InstanceId, dealt.CardSourceInstanceId);
        Assert.Equal(StrikeDef.Id, dealt.CardSourceId);
        Assert.Equal(new HistoryDamageResult(enemy.CombatId, ValueProp.Move, 0, 6, 1, false, false, true), dealt.Result);
        var taken = history.DamageReceivedEntries[1];
        Assert.Equal(CombatHistory.NoId, taken.CardSourceInstanceId);
        Assert.Null(taken.CardSourceId);
        Assert.Equal(CombatSide.Enemy, taken.DealerSide);
        Assert.Equal(7, taken.Result.TotalDamage, "total = blocked + unblocked");

        var attacked = history.CreatureAttackedEntries[0];
        Assert.Equal(player.CombatId, attacked.AttackerId, "creature attacked: the actor is the attacker");
        Assert.Equal(1, attacked.DamageResults.Count);
        Assert.Equal(6, attacked.DamageResults[0].UnblockedDamage);

        Assert.Equal(1, history.EnergySpentEntries[0].Amount);
        Assert.Equal(player.CombatId, history.EnergySpentEntries[0].Stamp.ActorId);
        Assert.True(history.CardDrawnEntries[0].FromHandDraw);
        Assert.Equal(CardType.Attack, history.CardDrawnEntries[0].CardType, "a drawn card's type is snapshotted");
        Assert.Equal(strike.InstanceId, history.CardDiscardedEntries[0].CardInstanceId);
        Assert.Equal(strike.InstanceId, history.CardExhaustedEntries[0].CardInstanceId);
        Assert.Equal(player.CombatId, history.CardGeneratedEntries[0].CreatorId);
        Assert.Equal("STATE_KERNEL_AFFLICTION", history.CardAfflictedEntries[0].AfflictionId);

        var moved = history.MonsterPerformedMoveEntries[0];
        Assert.Equal(enemy.CombatId, moved.MonsterCombatId, "monster move: the actor is the monster");
        Assert.Equal(EnemyDef.Id, moved.MonsterId);
        Assert.Equal("STATE_KERNEL_MOVE", moved.MoveId);
        Assert.SequenceEqual(new[] { player.CombatId }, moved.TargetIds!);
        Assert.SequenceEqual(new[] { player.CombatId }, moved.TargetIdList!.Value);

        Assert.Equal(player.CombatId, history.OrbChanneledEntries[0].Stamp.ActorId, "orb: the actor is the orb's owner");
        Assert.Equal("STATE_KERNEL_ORB", history.OrbChanneledEntries[0].OrbId);
        Assert.Equal(enemy.CombatId, history.PotionUsedEntries[0].TargetId);
        Assert.Equal(player.CombatId, history.PotionUsedEntries[0].Stamp.ActorId, "potion: the actor is the potion's owner");
        var received = history.PowerReceivedEntries[0];
        Assert.Equal(enemy.CombatId, received.Stamp.ActorId, "power received: the actor is the power's owner");
        Assert.Equal(2.5m, received.Amount);
        Assert.Equal(player.CombatId, received.ApplierId);
        Assert.Equal(-2, history.StarsModifiedEntries[0].Amount);
        Assert.Equal(4, history.SummonedEntries[0].Amount);
        Assert.Equal(player.CombatId, history.SummonedEntries[0].Stamp.ActorId);
        Assert.True(pet.IsPet);
    }

    public static void Test_State_HistoryActorsOfOwnerlessModelsAndAbsentArguments()
    {
        var cs = NewCombat();
        var history = cs.History;
        var loose = new CardInstance(StrikeDef, cs.AllocateCardInstanceId());
        var play = NewPlay(cs, loose, null, default);
        history.AppendCardPlayStarted(cs, play);
        Assert.Equal(CombatHistory.NoId, history.CardPlaysStarted[0].Stamp.ActorId);
        Assert.Equal(CombatHistory.NoId, history.CardPlaysStarted[0].CardPlay.PlayerId);
        Assert.Equal(CombatHistory.NoId, history.CardPlaysStarted[0].CardPlay.TargetId);
        var power = new KernelPower();
        history.AppendPowerReceived(cs, power, 1m, null);
        Assert.Equal(CombatHistory.NoId, history.PowerReceivedEntries[0].Stamp.ActorId, "an ownerless power has no actor (the game stores a null Actor)");
        Assert.Equal(CombatHistory.NoId, history.PowerReceivedEntries[0].ApplierId);
        history.AppendCardGenerated(cs, loose, null);
        Assert.Equal(CombatHistory.NoId, history.CardGeneratedEntries[0].CreatorId);
        history.AppendDamageReceived(cs, cs.Player, null, new DamageResult(cs.Player, ValueProp.Unpowered) { UnblockedDamage = 2 }, null);
        var taken = history.DamageReceivedEntries[0];
        Assert.Equal(CombatHistory.NoId, taken.DealerId);
        Assert.False(taken.HasDealer);
        Assert.Equal(CombatSide.None, taken.DealerSide);
        history.AppendMonsterPerformedMove(cs, AddEnemy(cs, 3), new MoveStateDef("STATE_KERNEL_IDLE", static (_, _, _) => { }), null);
        Assert.Null(history.MonsterPerformedMoveEntries[0].TargetIds, "null targets stay null");
        history.AppendCreatureAttacked(cs, cs.Player, Array.Empty<DamageResult>());
        Assert.Equal(0, history.CreatureAttackedEntries[0].DamageResults.Count);
    }

    public static void Test_State_HistoryFailedAppendLeavesTheLogUnchanged()
    {
        var cs = NewCombat();
        var history = cs.History;
        var card = AddCard(cs, cs.Player.Hand, StrikeDef);
        history.AppendCardDrawn(cs, card, false);
        ulong hash = StateHasher.Hash(cs);
        Assert.Throws<NullReferenceException>(() => history.AppendCardAfflicted(cs, card, null!));
        Assert.Throws<ArgumentNullException>(() => history.AppendDamageReceived(cs, cs.Player, null, null!, null));
        Assert.Throws<NullReferenceException>(() => history.AppendCardDrawn(null!, card, true));
        Assert.Equal(1, history.Count, "a failed append leaves the count unchanged");
        Assert.Equal(hash, StateHasher.Hash(cs), "a failed append leaves the state unchanged");
        history.AppendCardDrawn(cs, card, true);
        Assert.Equal(1, history.CardDrawnEntries[1].Stamp.Sequence, "the sequence continues without a gap");
    }

    public static void Test_State_HistoryHappenedThisTurnMatchesTheGame()
    {
        var cs = NewCombat();
        cs.RoundNumber = 2;
        cs.CurrentSide = CombatSide.Player;
        cs.Player.TurnNumber = 2;
        cs.History.AppendEnergySpent(cs, 1, cs.Player);
        var entry = cs.History.EnergySpentEntries[0];
        Assert.True(entry.HappenedThisTurn(cs), "same round, side and player turn");
        Assert.True(entry.Stamp.HappenedThisTurn(cs));
        Assert.False(entry.HappenedThisTurn(null), "a null combat gives false");
        cs.CurrentSide = CombatSide.Enemy;
        Assert.False(entry.HappenedThisTurn(cs), "another side gives false");
        cs.CurrentSide = CombatSide.Player;
        cs.RoundNumber = 3;
        Assert.False(entry.HappenedThisTurn(cs), "another round gives false");
        cs.RoundNumber = 2;
        cs.Player.TurnNumber = 3;
        Assert.False(entry.HappenedThisTurn(cs), "another player turn number gives false");
        cs.Player.TurnNumber = 2;
        var clone = cs.DeepClone();
        Assert.True(clone.History.EnergySpentEntries[0].HappenedThisTurn(clone), "the stamp holds values, so a clone answers the same");
        Assert.Equal(1, cs.History.CountThisTurn<EnergySpentEntry>(cs));
        Assert.True(cs.History.AnyThisTurn<EnergySpentEntry>(cs));
    }

    public static void Test_State_HistoryLastPlayerTurnHasNoSideCheck()
    {
        var cs = NewCombat();
        var player = cs.Player;
        cs.RoundNumber = 1;
        cs.CurrentSide = CombatSide.Player;
        player.TurnNumber = 1;
        var history = cs.History;
        history.AppendDamageReceived(cs, player, null, new DamageResult(player, ValueProp.Move) { UnblockedDamage = 1 }, null);
        cs.CurrentSide = CombatSide.Enemy;
        history.AppendDamageReceived(cs, player, null, new DamageResult(player, ValueProp.Move) { UnblockedDamage = 2 }, null);
        cs.RoundNumber = 2;
        cs.CurrentSide = CombatSide.Player;
        player.TurnNumber = 2;
        history.AppendDamageReceived(cs, player, null, new DamageResult(player, ValueProp.Move) { UnblockedDamage = 3 }, null);
        var entries = history.DamageReceivedEntries;
        Assert.True(entries[0].HappenedLastPlayerTurn(player), "player turn 1 is the last player turn during turn 2");
        Assert.True(entries[1].HappenedLastPlayerTurn(player), "the enemy turn after player turn 1 also counts (no side check)");
        Assert.False(entries[2].HappenedLastPlayerTurn(player), "this turn is not the last player turn");
        Assert.False(entries[0].HappenedThisTurn(cs) || entries[1].HappenedThisTurn(cs));
        Assert.True(entries[2].HappenedThisTurn(cs));
        Assert.Equal(2, history.CountLastPlayerTurn<DamageReceivedEntry>(player));
        Assert.Equal(1, history.CountLastPlayerTurn<DamageReceivedEntry>(player, e => e.Result.UnblockedDamage == 2));
        Assert.True(history.AnyLastPlayerTurn<DamageReceivedEntry>(player, e => e.Result.UnblockedDamage == 1));
        Assert.False(history.AnyLastPlayerTurn<DamageReceivedEntry>(player, e => e.Result.UnblockedDamage == 3));
        player.TurnNumber = 3;
        Assert.Equal(0, history.CountLastPlayerTurn<DamageReceivedEntry>(player, e => e.Result.UnblockedDamage < 3));
        Assert.Equal(1, history.CountLastPlayerTurn<DamageReceivedEntry>(player));
    }

    public static void Test_State_HistoryQueriesCountAnySumLastAndByActor()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var enemy = AddEnemy(cs, 40);
        var pet = AddPet(cs, OstyDef, 5);
        var history = cs.History;
        cs.RoundNumber = 1;
        player.TurnNumber = 1;
        var strike = AddCard(cs, player.Hand, StrikeDef);
        var guard = AddCard(cs, player.Hand, GuardDef);
        history.AppendCardPlayFinished(cs, NewPlay(cs, strike, enemy, new ResourceInfo(1, 1, 0, 0)));
        history.AppendEnergySpent(cs, 1, player);
        history.AppendStarsModified(cs, 2, player);
        cs.RoundNumber = 2;
        player.TurnNumber = 2;
        history.AppendCardPlayFinished(cs, NewPlay(cs, guard, null, new ResourceInfo(1, 1, 0, 0)));
        history.AppendCardPlayFinished(cs, NewPlay(cs, strike, enemy, new ResourceInfo(0, 1, 0, 0)));
        history.AppendEnergySpent(cs, 2, player);
        history.AppendEnergySpent(cs, 1, player);
        history.AppendStarsModified(cs, 3, player);
        history.AppendStarsModified(cs, -1, player);
        history.AppendCreatureAttacked(cs, pet, new[] { new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 3 } });
        history.AppendCreatureAttacked(cs, player, new[] { new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 6 } });

        Assert.Equal(3, history.CountOf<CardPlayFinishedEntry>(), "whole-combat count (GoldAxe)");
        Assert.Equal(2, history.CountOf<CardPlayFinishedEntry>(e => e.CardPlay.CardType == CardType.Attack));
        Assert.Equal(2, history.CountThisTurn<CardPlayFinishedEntry>(cs), "this-turn count (Ftl)");
        Assert.Equal(1, history.CountThisTurn<CardPlayFinishedEntry>(cs, e => e.CardPlay.CardType == CardType.Attack), "this-turn attacks (Finisher)");
        Assert.True(history.AnyOf<CardPlayFinishedEntry>(e => e.CardPlay.IsPlayOf(guard)), "card identity by instance id (Fetch)");
        Assert.False(history.AnyOf<CardPlayFinishedEntry>(e => e.CardPlay.CardType == CardType.Power));
        Assert.False(history.AnyOf<SummonedEntry>(), "no entry of an unused type");
        Assert.Equal(3, history.SumThisTurn<EnergySpentEntry>(cs, e => e.Amount), "this-turn energy sum (HelixDrill)");
        Assert.Equal(4, history.SumOf<EnergySpentEntry>(e => e.Amount));
        Assert.Equal(3, history.SumThisTurn<StarsModifiedEntry>(cs, e => e.Amount, e => e.Amount > 0), "positive star gains this turn (Radiate)");
        var last = history.LastOf<CardPlayFinishedEntry>(e => e.CardPlay.CardType == CardType.Attack);
        Assert.True(last.HasValue && last.Value.CardPlay.Resources.EnergySpent == 0, "the last matching entry (HistoryCourse)");
        Assert.Null(history.LastOf<CardPlayFinishedEntry>(e => e.CardPlay.CardType == CardType.Curse));
        Assert.Equal(1, history.CountByActor<CreatureAttackedEntry>(pet.CombatId), "Osty's attacks (Flatten, Rattle)");
        Assert.Equal(1, history.CountByActor<CreatureAttackedEntry>(pet.CombatId, e => e.HappenedThisTurn(cs)));
        Assert.SequenceEqual(new[] { player.CombatId }, history.ByActor<CreatureAttackedEntry>(player.CombatId).Select(e => e.AttackerId));
        Assert.Equal(0, history.ByActor<CreatureAttackedEntry>(enemy.CombatId).Count());
        Assert.True(history.CreatureAttackedEntries[0].IsByActor(pet));
        Assert.False(history.CreatureAttackedEntries[0].IsByActor(null));
        Assert.Equal(history.CardPlaysFinished.Count, history.Entries<CardPlayFinishedEntry>().Count);
        Assert.True(ReferenceEquals(history.CardPlaysFinished, history.Entries<CardPlayFinishedEntry>()), "Entries<T> is the per-type list");
        Assert.Throws<ArgumentException>(() => history.Entries<NotAnEntry>());
    }

    public static void Test_State_HistoryCardPlayComparesByPlayId()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var defend = AddCard(cs, player.Hand, GuardDef);
        var first = NewPlay(cs, defend, null, new ResourceInfo(1, 1, 0, 0));
        var second = NewPlay(cs, defend, null, new ResourceInfo(1, 1, 0, 0));
        var a = HistoryCardPlay.From(first);
        var b = HistoryCardPlay.From(second);
        Assert.NotEqual(a.PlayId, b.PlayId);
        Assert.False(a == b, "two plays of the same card with equal fields are different plays");
        Assert.True(a == HistoryCardPlay.From(first), "a play equals itself");
        Assert.Equal(a.GetHashCode(), HistoryCardPlay.From(first).GetHashCode());
        Assert.True(a.IsSamePlay(first) && !a.IsSamePlay(second));
        Assert.True(a.IsPlayOf(defend) && b.IsPlayOf(defend));
        var history = cs.History;
        history.AppendBlockGained(cs, player, 5, ValueProp.Move, first);
        history.AppendBlockGained(cs, player, 5, ValueProp.Move, second);
        history.AppendBlockGained(cs, player, 2, ValueProp.Unpowered, null);
        int otherPlays = history.CountThisTurn<BlockGainedEntry>(cs, e => e.CardPlay is { } p && p.PlayerId == player.CombatId && !p.IsSamePlay(second));
        Assert.Equal(1, otherPlays, "Unmovable counts this turn's block from plays other than the current one");
        Assert.Equal(cs.NextCardPlayId - 1, second.PlayId);
    }

    public static void Test_State_HistorySnapshotsDupeAndCardTypeAtAppend()
    {
        var cs = NewCombat();
        var dupe = AddCard(cs, cs.Player.Hand, StrikeDef);
        dupe.IsDupe = true;
        var slime = AddCard(cs, cs.Player.DrawPile, SlimeDef);
        cs.History.AppendCardPlayFinished(cs, NewPlay(cs, dupe, null, default));
        cs.History.AppendCardDrawn(cs, slime, false);
        cs.Player.Hand.RemoveInternal(dupe);
        cs.Player.DrawPile.RemoveInternal(slime);
        var finished = cs.History.CardPlaysFinished[0];
        Assert.True(finished.CardPlay.IsDupe, "IsDupe is read at append and survives the card leaving combat");
        Assert.Equal(CardType.Attack, finished.CardPlay.CardType);
        Assert.Equal(CardType.Status, cs.History.CardDrawnEntries[0].CardType, "a drawn status card stays identifiable (IterationPower)");
    }

    public static void Test_State_HistoryWasEtherealReadsTheInCombatKeywords()
    {
        var cs = NewCombat();
        var ethereal = AddCard(cs, cs.Player.Hand, StrikeDef);
        ethereal.AddKeyword(CardKeyword.Ethereal);
        var plain = AddCard(cs, cs.Player.Hand, StrikeDef);
        cs.History.AppendCardPlayFinished(cs, NewPlay(cs, ethereal, null, default));
        cs.History.AppendCardPlayFinished(cs, NewPlay(cs, plain, null, default));
        AttachPower(new EtherealPower(), cs.Player, 1);
        cs.History.AppendCardPlayFinished(cs, NewPlay(cs, plain, null, default));
        var entries = cs.History.CardPlaysFinished;
        Assert.True(entries[0].WasEthereal, "a local Ethereal keyword");
        Assert.False(entries[1].WasEthereal);
        Assert.True(entries[2].WasEthereal, "a listener that adds Ethereal in combat");
        Assert.False(plain.HasKeyword(CardKeyword.Ethereal), "the in-combat keyword set does not change the card");
        Assert.Equal(1, cs.History.CountOf<CardPlayFinishedEntry>(e => !e.WasEthereal));
    }

    public static void Test_State_HistoryCloneCopiesEntriesAndInnerLists()
    {
        var cs = NewCombat();
        var enemy = AddEnemy(cs, 20);
        var history = cs.History;
        history.AppendCreatureAttacked(cs, cs.Player, new[] { new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 5 }, new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 4 } });
        history.AppendMonsterPerformedMove(cs, enemy, new MoveStateDef("STATE_KERNEL_BITE", static (_, _, _) => { }), new CreatureState[] { cs.Player });
        history.AppendEnergySpent(cs, 2, cs.Player);
        var clone = cs.DeepClone();
        var copy = clone.History;
        AssertNoSharedMutable(history, copy, "history with inner lists");
        Assert.SequenceEqual(history.CreatureAttackedEntries[0].DamageResults, copy.CreatureAttackedEntries[0].DamageResults);
        Assert.SequenceEqual(history.MonsterPerformedMoveEntries[0].TargetIds!, copy.MonsterPerformedMoveEntries[0].TargetIds!);
        Assert.True(history.CreatureAttackedEntries[0] == copy.CreatureAttackedEntries[0], "entries compare by content across clones");
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone));
        ulong sourceHash = StateHasher.Hash(cs);
        var sourceResults = history.CreatureAttackedEntries[0].DamageResultList.ToArray();
        copy.AppendCreatureAttacked(clone, clone.Player, new[] { new DamageResult(clone.Monsters[0], ValueProp.Move) { UnblockedDamage = 9 } });
        copy.AppendMonsterPerformedMove(clone, clone.Monsters[0], new MoveStateDef("STATE_KERNEL_GNAW", static (_, _, _) => { }), new CreatureState[] { clone.Player, clone.Monsters[0] });
        Assert.SequenceEqual(sourceResults, history.CreatureAttackedEntries[0].DamageResults, "inner-list appends on the clone leave the source's lists unchanged");
        Assert.Equal(1, history.CreatureAttackedEntries.Count);
        Assert.Equal(9, copy.CreatureAttackedEntries[1].DamageResults[0].UnblockedDamage);
        Assert.SequenceEqual(new[] { clone.Player.CombatId, clone.Monsters[0].CombatId }, copy.MonsterPerformedMoveEntries[1].TargetIds!);
        Assert.Equal(sourceHash, StateHasher.Hash(cs), "appends on a clone leave the source's hash unchanged");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = history.CreatureAttackedEntries[0].DamageResultList[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = history.CreatureAttackedEntries[0].DamageResults[2]);
        copy.AppendEnergySpent(clone, 1, clone.Player);
        copy.AppendEnergySpent(clone, 1, clone.Player);
        Assert.Equal(7, copy.Count);
        Assert.Equal(3, history.Count, "an append on the clone is not visible in the source");
        Assert.Equal(1, history.EnergySpentEntries.Count);
        Assert.Equal(5, copy.EnergySpentEntries[1].Stamp.Sequence, "the clone's sequence continues from the source");
        Assert.Equal(6, copy.EnergySpentEntries[2].Stamp.Sequence);
        history.AppendEnergySpent(cs, 7, cs.Player);
        Assert.Equal(3, copy.EnergySpentEntries.Count, "an append on the source is not visible in the clone");
        Assert.Equal(3, history.EnergySpentEntries[1].Stamp.Sequence, "the source continues its own sequence");
        var stale = history.CreatureAttackedEntries[0];
        history.Clear();
        Assert.Equal(0, history.Count);
        Assert.Equal(0, history.EnergySpentEntries.Count);
        Assert.SequenceEqual(sourceResults, stale.DamageResults, "an entry read before Clear keeps its inner list");
        Assert.Equal(7, copy.Count, "clearing the source leaves the clone intact");
        Assert.Equal(2, copy.CreatureAttackedEntries[0].DamageResults.Count);
        history.AppendEnergySpent(cs, 1, cs.Player);
        Assert.Equal(0, history.EnergySpentEntries[0].Stamp.Sequence, "Clear restarts the sequence");
    }

    public static void Test_State_HistoryClearMatchesAFreshLog()
    {
        var cs = NewCombat();
        var empty = StateHasher.CanonicalBytes(cs.History);
        FillHistory(cs, 60, 3UL);
        Assert.Equal(60, cs.History.Count);
        cs.History.Clear();
        Assert.True(empty.AsSpan().SequenceEqual(StateHasher.CanonicalBytes(cs.History)), "a cleared log writes the same bytes as a fresh one");
        Assert.True(empty.AsSpan().SequenceEqual(StateHasher.CanonicalBytes(cs.History.DeepClone())));
    }

    public static void Test_State_HistoryCloneOf500EntriesStaysWithinBudget()
    {
        var cs = NewCombat();
        FillHistory(cs, HistoryCloneEntries, 11UL);
        var history = cs.History;
        Assert.Equal(HistoryCloneEntries, history.Count);
        for (int i = 0; i < 200; i++)
        {
            GC.KeepAlive(history.DeepClone());
        }
        double best = double.MaxValue;
        const int batches = 25;
        const int perBatch = 100;
        for (int b = 0; b < batches; b++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < perBatch; i++)
            {
                GC.KeepAlive(history.DeepClone());
            }
            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1000.0 / perBatch);
        }
        Console.WriteLine($"     State history clone: {HistoryCloneEntries} entries, best batch mean {best:F2} us per DeepClone ({batches} batches of {perBatch}, this build's configuration)");
        Assert.True(best <= HistoryCloneBudgetMicroseconds, $"a {HistoryCloneEntries}-entry history clone took {best:F2} us (budget {HistoryCloneBudgetMicroseconds} us)");
        Assert.Equal(StateHasher.Hash(history), StateHasher.Hash(history.DeepClone()));
    }

    public static void Test_State_MonsterListenerCanonicalProtocol()
    {
        var none = BytesOf(w => ListenerCanonical.Write(w, null));
        Assert.SequenceEqual(new byte[] { 0 }, none, "no listener writes 0, the P0 'false'");
        var stateless = BytesOf(w => ListenerCanonical.Write(w, new StatelessListener()));
        Assert.SequenceEqual(new byte[] { 1 }, stateless, "an immutable adapter writes 1, the P0 'true'");
        var stateful = new StatefulListener(4, 2);
        var bytes = BytesOf(w => ListenerCanonical.Write(w, stateful));
        Assert.Equal((byte)2, bytes[0], "a stateful adapter writes 2 then its own form");
        Assert.True(bytes.Length > 1);

        var cs = NewCombat();
        var monster = AddEnemy(cs, 12);
        ulong bare = StateHasher.Hash(cs);
        monster.Listener = new StatelessListener();
        ulong withStateless = StateHasher.Hash(cs);
        Assert.NotEqual(bare, withStateless, "listener presence changes the hash");
        monster.Listener = new StatefulListener(monster.CombatId, 1);
        ulong withStateful = StateHasher.Hash(cs);
        Assert.NotEqual(withStateless, withStateful);
        ((StatefulListener)monster.Listener).Counter = 2;
        Assert.NotEqual(withStateful, StateHasher.Hash(cs), "a stateful adapter's state reaches the hash");
        var clone = cs.DeepClone();
        var cloned = (StatefulListener)clone.Monsters[0].Listener!;
        Assert.False(ReferenceEquals(monster.Listener, cloned), "an owned listener is cloned per owner");
        Assert.Equal(clone.Monsters[0].CombatId, cloned.OwnerId);
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone));
        ulong source = StateHasher.Hash(cs);
        cloned.Counter = 9;
        Assert.Equal(source, StateHasher.Hash(cs), "the clone's listener state is its own");
        var stateless2 = new StatelessListener();
        monster.Listener = stateless2;
        Assert.True(ReferenceEquals(stateless2, cs.DeepClone().Monsters[0].Listener), "an immutable adapter is shared by clones");
    }

    public static void Test_State_FidelityLogRecordsInOrderAndHashes()
    {
        var cs = NewCombat();
        var log = cs.Run.Fidelity;
        ulong empty = StateHasher.Hash(cs);
        log.Record(FidelityFlag.ApproximatedRule, "STATE_KERNEL:First");
        log.Record(FidelityFlag.DeferredListenerPromptInline, "STATE_KERNEL:Second");
        Assert.Equal(2, log.Count);
        Assert.Equal(new FidelityEntry(FidelityFlag.ApproximatedRule, "STATE_KERNEL:First"), log.Entries[0]);
        Assert.True(log.Contains(FidelityFlag.DeferredListenerPromptInline));
        Assert.True(log.Contains(FidelityFlag.ApproximatedRule, "STATE_KERNEL:First"));
        Assert.False(log.Contains(FidelityFlag.ApproximatedRule, "STATE_KERNEL:Second"));
        Assert.Equal(1, log.CountOf(FidelityFlag.ApproximatedRule));
        ulong two = StateHasher.Hash(cs);
        Assert.NotEqual(empty, two);
        var clone = cs.DeepClone();
        Assert.Equal(two, StateHasher.Hash(clone));
        clone.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, "STATE_KERNEL:Clone");
        Assert.Equal(two, StateHasher.Hash(cs), "a flag recorded on a clone stays there");
        Assert.Equal(2, log.Count);
        Assert.Throws<ArgumentNullException>(() => log.Record(FidelityFlag.ApproximatedRule, null!));
        var reordered = NewCombat();
        reordered.Run.Fidelity.Record(FidelityFlag.DeferredListenerPromptInline, "STATE_KERNEL:Second");
        reordered.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, "STATE_KERNEL:First");
        Assert.NotEqual(StateHasher.Hash(reordered.Run.Fidelity), StateHasher.Hash(log), "the log order is part of the hash");
    }

    public static void Test_State_KernelHashCoversEveryNewMember()
    {
        var baseState = BuildRichState(21UL);
        ulong baseHash = StateHasher.Hash(baseState);
        var probes = new (string Name, Action<CombatState> Mutate)[]
        {
            ("gold", s => s.Player.Gold += 1),
            ("base orb slots", s => s.Player.BaseOrbSlotCount += 1),
            ("can use potions", s => s.Player.CanUseOrRemovePotions = !s.Player.CanUseOrRemovePotions),
            ("ascension effects flag", s => s.Player.AscensionEffectsApplied = !s.Player.AscensionEffectsApplied),
            ("relic counter", s => ((KernelRelic)s.Player.Relics[0]).Counter++),
            ("relic log", s => ((KernelRelic)s.Player.Relics[0]).Log.Add(5)),
            ("relic order", s => { var r = s.Player.Relics; (r[0], r[1]) = (r[1], r[0]); }),
            ("relic removed", s => s.Player.RemoveRelicInternal(s.Player.Relics[0])),
            ("relic melted", s => s.Player.MeltRelicInternal(s.Player.GetRelic<KernelWaxRelic>()!)),
            ("relic status", s => s.Player.Relics[0].Status = RelicStatus.Active),
            ("relic stack", s => s.Player.Relics[0].IncrementStackCount()),
            ("potion charges", s => ((KernelPotion)s.Player.PotionSlots[0]!).Charges++),
            ("potion slot moved", s => { var slots = s.Player.PotionSlots; (slots[0], slots[1]) = (slots[1], slots[0]); }),
            ("potion discarded", s => s.Player.DiscardPotionInternal(s.Player.PotionSlots[0]!)),
            ("potion slot added", s => s.Player.AddToMaxPotionCount(1)),
            ("deck card added", s => s.Player.Deck.Add(s.CreateCard(GuardDef, s.Player))),
            ("deck order", s => { var d = s.Player.Deck; (d[0], d[1]) = (d[1], d[0]); }),
            ("deck card upgraded", s => s.Player.Deck[0].Upgrade()),
            ("pet ids", s => s.Player.PetIds.Clear()),
            ("pet hp", s => s.Allies[1].CurrentHp -= 1),
            ("pet power", s => AttachPower(new KernelPower(), s.Allies[1], 1)),
            ("pet owner", s => ((MonsterState)s.Allies[1]).PetOwnerId = PowerModel.NoCreature),
            ("pet added", s => AddPet(s, PetDef, 2)),
            ("ally removed", s => s.RemoveAlly((MonsterState)s.Allies[s.Allies.Count - 1])),
            ("monster listener state", s => ((StatefulListener)((MonsterState)s.Allies[1]).Listener!).Counter++),
            ("monster listener removed", s => s.Monsters[0].Listener = null),
            ("effect depth", s => s.CardOrPotionEffectDepth += 1),
            ("card play id", s => s.AllocateCardPlayId()),
            ("extra turn", s => s.PlayersTakingExtraTurn.Clear()),
            ("card instance allocator", s => s.AllocateCardInstanceId()),
            ("history energy", s => s.History.AppendEnergySpent(s, 1, s.Player)),
            ("history stars", s => s.History.AppendStarsModified(s, 1, s.Player)),
            ("history summoned", s => s.History.AppendSummoned(s, 1, s.Player)),
            ("history draw", s => s.History.AppendCardDrawn(s, s.Player.Hand[0], true)),
            ("history discard", s => s.History.AppendCardDiscarded(s, s.Player.Hand[0])),
            ("history exhaust", s => s.History.AppendCardExhausted(s, s.Player.Hand[0])),
            ("history generated", s => s.History.AppendCardGenerated(s, s.Player.Hand[0], null)),
            ("history afflicted", s => s.History.AppendCardAfflicted(s, s.Player.Hand[0], new KernelAffliction())),
            ("history play started", s => s.History.AppendCardPlayStarted(s, NewPlay(s, s.Player.Hand[0], null, default))),
            ("history play finished", s => s.History.AppendCardPlayFinished(s, NewPlay(s, s.Player.Hand[0], null, default))),
            ("history block", s => s.History.AppendBlockGained(s, s.Player, 3, ValueProp.Move, null)),
            ("history damage", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Move), null)),
            ("history attacked", s => s.History.AppendCreatureAttacked(s, s.Player, Array.Empty<DamageResult>())),
            ("history move", s => s.History.AppendMonsterPerformedMove(s, s.Monsters[0], new MoveStateDef("STATE_KERNEL_PROBE", static (_, _, _) => { }), null)),
            ("history orb", s => { var orb = new KernelOrb(); orb.AttachTo(s.Player); s.History.AppendOrbChanneled(s, orb); }),
            ("history potion", s => s.History.AppendPotionUsed(s, s.Player.PotionSlots[0]!, null)),
            ("history power", s => s.History.AppendPowerReceived(s, s.Player.Powers[0], 1m, null)),
            ("history cleared", s => s.History.Clear()),
            ("fidelity", s => s.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, "STATE_KERNEL:Probe")),
            ("selector popped", s => s.Choice.PopSelector()),
            ("selector pushed", s => s.Choice.PushSelector(new KernelSelector("probe"))),
            ("player stars", s => s.Player.Stars += 1),
            ("player hand card", s => s.Player.Hand.RemoveAtInternal(0)),
        };
        var seen = new Dictionary<ulong, string> { [baseHash] = "base" };
        foreach (var (name, mutate) in probes)
        {
            var probe = baseState.DeepClone();
            Assert.Equal(baseHash, StateHasher.Hash(probe), $"clone before the '{name}' probe");
            mutate(probe);
            ulong h = StateHasher.Hash(probe);
            Assert.NotEqual(baseHash, h, $"mutating {name} did not change the hash");
            Assert.Equal(baseHash, StateHasher.Hash(baseState), $"mutating {name} on a clone changed the source");
            Assert.True(seen.TryAdd(h, name), $"the '{name}' probe collides with '{(seen.TryGetValue(h, out var other) ? other : "?")}'");
        }
    }

    public static void Test_State_KernelCloneHashEqualityAtRandomPoints()
    {
        foreach (ulong seed in new[] { 1UL, 2UL, 3UL, 4UL, 5UL })
        {
            var policy = new KernelPolicy(seed * 0x9E3779B97F4A7C15UL + 17UL);
            var cs = BuildRichState(seed);
            int checks = 0;
            for (int step = 0; step < 160; step++)
            {
                RandomMutation(cs, policy);
                if (policy.OneIn(4))
                {
                    CheckCloneIndependence(cs, policy, $"seed {seed} step {step}");
                    checks++;
                }
            }
            CheckCloneIndependence(cs, policy, $"seed {seed} final");
            Assert.True(checks > 10, $"seed {seed}: too few clone checks ({checks})");
        }
    }

    public static void Test_State_KernelCloneSharesNoMutableObjects()
    {
        var cs = BuildRichState(31UL);
        var policy = new KernelPolicy(31UL);
        for (int i = 0; i < 80; i++)
        {
            RandomMutation(cs, policy);
        }
        AssertNoSharedMutable(cs, cs.DeepClone(), "random rich state");
        AssertNoSharedMutable(cs.Run, cs.Run.DeepClone(), "random rich run");
    }

    public static void Test_State_KernelOrbStatesCloneAndHash()
    {
        W1Pending.Run(nameof(Test_State_KernelOrbStatesCloneAndHash), "character-mechanics", () =>
        {
            var cs = BuildRichState(41UL);
            var queue = cs.Player.OrbQueue;
            queue.AddCapacity(3);
            var first = new KernelOrb { Charge = 1 };
            first.AttachTo(cs.Player);
            var second = new KernelOrb { Charge = 2 };
            second.AttachTo(cs.Player);
            Assert.True(queue.TryEnqueue(first));
            Assert.True(queue.TryEnqueue(second));
            ulong hash = StateHasher.Hash(cs);
            var clone = cs.DeepClone();
            Assert.Equal(hash, StateHasher.Hash(clone), "a state with orbs hashes like its clone");
            Assert.Equal(2, clone.Player.OrbQueue.Orbs.Count);
            Assert.True(ReferenceEquals(clone.Player, clone.Player.OrbQueue.Orbs[0].Owner), "orbs are rebound to the cloned player");
            ((KernelOrb)clone.Player.OrbQueue.Orbs[1]).Charge = 7;
            Assert.Equal(hash, StateHasher.Hash(cs), "an orb's state on the clone is its own");
            Assert.NotEqual(hash, StateHasher.Hash(clone), "an orb's state reaches the hash");
            var capacity = cs.DeepClone();
            capacity.Player.OrbQueue.AddCapacity(1);
            Assert.NotEqual(hash, StateHasher.Hash(capacity), "the orb capacity reaches the hash");
            AssertNoSharedMutable(cs, cs.DeepClone(), "state with orbs");
        });
    }

    public static void Test_State_KernelPendingSelectorClonesWithTheRun()
    {
        var cs = BuildRichState(51UL);
        Assert.Equal(1, cs.Choice.Selectors.Count, "the rich state holds a pending selector");
        var clone = cs.DeepClone();
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone));
        Assert.Equal(1, clone.Choice.Selectors.Count);
        clone.Choice.PushSelector(new KernelSelector("clone-only"));
        Assert.Equal(1, cs.Choice.Selectors.Count, "pushing a selector on the clone leaves the source's stack");
        clone.Choice.PopSelector();
        clone.Choice.PopSelector();
        Assert.Equal(1, cs.Choice.Selectors.Count, "popping on the clone leaves the source's stack");
        Assert.NotEqual(StateHasher.Hash(cs), StateHasher.Hash(clone));
    }

    public static void Test_State_KernelCloneHashEqualityAtRandomPointsWithOrbs()
    {
        W1Pending.Run(nameof(Test_State_KernelCloneHashEqualityAtRandomPointsWithOrbs), "character-mechanics", () =>
        {
            foreach (ulong seed in new[] { 6UL, 7UL, 8UL })
            {
                var policy = new KernelPolicy(seed * 31UL + 5UL);
                var cs = BuildRichState(seed);
                cs.Player.OrbQueue.AddCapacity(3);
                for (int step = 0; step < 120; step++)
                {
                    if (policy.OneIn(3))
                    {
                        var queue = cs.Player.OrbQueue;
                        if (queue.Orbs.Count < queue.Capacity)
                        {
                            var orb = new KernelOrb { Charge = policy.Next(5) };
                            orb.AttachTo(cs.Player);
                            Assert.True(queue.TryEnqueue(orb));
                            cs.History.AppendOrbChanneled(cs, orb);
                        }
                        else
                        {
                            queue.Remove(queue.Orbs[0]);
                        }
                    }
                    else
                    {
                        RandomMutation(cs, policy);
                    }
                    if (policy.OneIn(4))
                    {
                        CheckCloneIndependence(cs, policy, $"orbs seed {seed} step {step}");
                    }
                }
                AssertNoSharedMutable(cs, cs.DeepClone(), $"orbs seed {seed}");
            }
        });
    }

    public static void Test_State_HistoryDigestIsIndependentOfWhenItIsHashed()
    {
        var eager = NewCombat();
        var lazy = NewCombat();
        var forked = NewCombat();
        AddEnemy(eager, 30);
        AddEnemy(lazy, 30);
        AddEnemy(forked, 30);
        var eagerPolicy = new KernelPolicy(77UL);
        var lazyPolicy = new KernelPolicy(77UL);
        var forkPolicy = new KernelPolicy(77UL);
        CombatState? fork = null;
        for (int step = 0; step < 90; step++)
        {
            AppendRandomEntry(eager, eagerPolicy);
            StateHasher.Hash(eager);
            AppendRandomEntry(lazy, lazyPolicy);
            AppendRandomEntry(forked, forkPolicy);
            if (step == 40)
            {
                StateHasher.Hash(forked);
                fork = forked.DeepClone();
            }
            if (fork != null && step > 40)
            {
                AppendRandomEntry(fork, new KernelPolicy(0UL));
            }
        }
        Assert.Equal(StateHasher.Hash(eager), StateHasher.Hash(lazy), "a log hashed after every append and a log hashed once at the end agree");
        Assert.Equal(StateHasher.Hash(eager), StateHasher.Hash(forked), "a log hashed once midway agrees as well");
        Assert.True(StateHasher.CanonicalBytes(eager).AsSpan().SequenceEqual(StateHasher.CanonicalBytes(lazy)));
        var eagerEntries = new CanonicalWriter();
        eager.History.WriteEntriesCanonical(eagerEntries);
        var lazyEntries = new CanonicalWriter();
        lazy.History.WriteEntriesCanonical(lazyEntries);
        Assert.True(eagerEntries.WrittenSpan.SequenceEqual(lazyEntries.WrittenSpan), "the full entry form agrees too");
        Assert.True(eagerEntries.Length > StateHasher.CanonicalBytes(eager.History).Length, "the full form carries every entry, the hashed form a digest per type");
        var clone = eager.DeepClone();
        AppendRandomEntry(clone, new KernelPolicy(5UL));
        AppendRandomEntry(eager, new KernelPolicy(5UL));
        Assert.Equal(StateHasher.Hash(eager), StateHasher.Hash(clone), "a clone that inherited the folded digests extends them the same way");
        var fresh = NewCombat();
        var empty = StateHasher.CanonicalBytes(fresh.History);
        Assert.Equal(2 + 4 + 17 * 4, empty.Length, "an empty log writes the tag, the count and seventeen zero counts");
        var emptyEntries = new CanonicalWriter();
        fresh.History.WriteEntriesCanonical(emptyEntries);
        Assert.True(emptyEntries.WrittenSpan.SequenceEqual(empty), "for an empty log both forms are the same bytes");
    }

    public static void Test_State_HistoryDigestCoversEveryEntryField()
    {
        var variants = new (string Name, Action<CombatState> Append)[]
        {
            ("block amount", s => s.History.AppendBlockGained(s, s.Player, 5, ValueProp.Move, null)),
            ("block amount 2", s => s.History.AppendBlockGained(s, s.Player, 6, ValueProp.Move, null)),
            ("block props", s => s.History.AppendBlockGained(s, s.Player, 5, ValueProp.Unpowered, null)),
            ("block receiver", s => s.History.AppendBlockGained(s, s.Monsters[0], 5, ValueProp.Move, null)),
            ("block play", s => s.History.AppendBlockGained(s, s.Player, 5, ValueProp.Move, NewPlay(s, s.Player.Hand[0], null, default))),
            ("afflicted", s => s.History.AppendCardAfflicted(s, s.Player.Hand[0], new KernelAffliction())),
            ("afflicted other card", s => s.History.AppendCardAfflicted(s, s.Player.Hand[1], new KernelAffliction())),
            ("discarded", s => s.History.AppendCardDiscarded(s, s.Player.Hand[0])),
            ("discarded other card", s => s.History.AppendCardDiscarded(s, s.Player.Hand[1])),
            ("drawn", s => s.History.AppendCardDrawn(s, s.Player.Hand[0], false)),
            ("drawn from hand draw", s => s.History.AppendCardDrawn(s, s.Player.Hand[0], true)),
            ("drawn other card", s => s.History.AppendCardDrawn(s, s.Player.Hand[1], false)),
            ("exhausted", s => s.History.AppendCardExhausted(s, s.Player.Hand[0])),
            ("generated", s => s.History.AppendCardGenerated(s, s.Player.Hand[0], null)),
            ("generated by player", s => s.History.AppendCardGenerated(s, s.Player.Hand[0], s.Player)),
            ("play started", s => s.History.AppendCardPlayStarted(s, NewPlay(s, s.Player.Hand[0], null, default))),
            ("play started target", s => s.History.AppendCardPlayStarted(s, NewPlay(s, s.Player.Hand[0], s.Monsters[0], default))),
            ("play started resources", s => s.History.AppendCardPlayStarted(s, NewPlay(s, s.Player.Hand[0], null, new ResourceInfo(0, 1, 0, 0)))),
            ("play started auto", s => { var p = NewPlay(s, s.Player.Hand[0], null, default); p.IsAutoPlay = true; s.History.AppendCardPlayStarted(s, p); }),
            ("play started index", s => { var p = NewPlay(s, s.Player.Hand[0], null, default); p.PlayCount = 2; p.PlayIndex = 1; s.History.AppendCardPlayStarted(s, p); }),
            ("play started pile", s => { var p = NewPlay(s, s.Player.Hand[0], null, default); p.ResultPile = PileType.Exhaust; s.History.AppendCardPlayStarted(s, p); }),
            ("play started dupe", s => { s.Player.Hand[0].IsDupe = true; s.History.AppendCardPlayStarted(s, NewPlay(s, s.Player.Hand[0], null, default)); }),
            ("play finished", s => s.History.AppendCardPlayFinished(s, NewPlay(s, s.Player.Hand[0], null, default))),
            ("play finished ethereal", s => { s.Player.Hand[0].AddKeyword(CardKeyword.Ethereal); s.History.AppendCardPlayFinished(s, NewPlay(s, s.Player.Hand[0], null, default)); }),
            ("attacked", s => s.History.AppendCreatureAttacked(s, s.Player, new[] { new DamageResult(s.Monsters[0], ValueProp.Move) { UnblockedDamage = 1 } })),
            ("attacked results", s => s.History.AppendCreatureAttacked(s, s.Player, new[] { new DamageResult(s.Monsters[0], ValueProp.Move) { UnblockedDamage = 2 } })),
            ("attacked killed", s => s.History.AppendCreatureAttacked(s, s.Player, new[] { new DamageResult(s.Monsters[0], ValueProp.Move) { UnblockedDamage = 1, WasTargetKilled = true } })),
            ("attacked by monster", s => s.History.AppendCreatureAttacked(s, s.Monsters[0], new[] { new DamageResult(s.Monsters[0], ValueProp.Move) { UnblockedDamage = 1 } })),
            ("damage", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 3 }, null)),
            ("damage blocked", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 3, BlockedDamage = 1 }, null)),
            ("damage overkill", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 3, OverkillDamage = 1 }, null)),
            ("damage broke block", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 3, WasBlockBroken = true }, null)),
            ("damage fully blocked", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 3, WasFullyBlocked = true }, null)),
            ("damage dealer", s => s.History.AppendDamageReceived(s, s.Player, s.Monsters[0], new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 3 }, null)),
            ("damage card", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 3 }, s.Player.Hand[0])),
            ("damage props", s => s.History.AppendDamageReceived(s, s.Player, null, new DamageResult(s.Player, ValueProp.Unblockable) { UnblockedDamage = 3 }, null)),
            ("energy", s => s.History.AppendEnergySpent(s, 1, s.Player)),
            ("energy 2", s => s.History.AppendEnergySpent(s, 2, s.Player)),
            ("move", s => s.History.AppendMonsterPerformedMove(s, s.Monsters[0], new MoveStateDef("STATE_KERNEL_A", static (_, _, _) => { }), null)),
            ("move id", s => s.History.AppendMonsterPerformedMove(s, s.Monsters[0], new MoveStateDef("STATE_KERNEL_B", static (_, _, _) => { }), null)),
            ("move empty targets", s => s.History.AppendMonsterPerformedMove(s, s.Monsters[0], new MoveStateDef("STATE_KERNEL_A", static (_, _, _) => { }), Array.Empty<CreatureState>())),
            ("move targets", s => s.History.AppendMonsterPerformedMove(s, s.Monsters[0], new MoveStateDef("STATE_KERNEL_A", static (_, _, _) => { }), new CreatureState[] { s.Player })),
            ("orb", s => { var orb = new KernelOrb(); orb.AttachTo(s.Player); s.History.AppendOrbChanneled(s, orb); }),
            ("orb ownerless", s => s.History.AppendOrbChanneled(s, new KernelOrb())),
            ("potion", s => s.History.AppendPotionUsed(s, new KernelPotion("STATE_KERNEL_POTION_X"), null)),
            ("potion target", s => s.History.AppendPotionUsed(s, new KernelPotion("STATE_KERNEL_POTION_X"), s.Monsters[0])),
            ("potion id", s => s.History.AppendPotionUsed(s, new KernelPotion("STATE_KERNEL_POTION_Y"), null)),
            ("power", s => s.History.AppendPowerReceived(s, s.Player.Powers[0], 1m, null)),
            ("power amount", s => s.History.AppendPowerReceived(s, s.Player.Powers[0], 1.5m, null)),
            ("power applier", s => s.History.AppendPowerReceived(s, s.Player.Powers[0], 1m, s.Monsters[0])),
            ("stars", s => s.History.AppendStarsModified(s, 1, s.Player)),
            ("stars negative", s => s.History.AppendStarsModified(s, -1, s.Player)),
            ("summoned", s => s.History.AppendSummoned(s, 1, s.Player)),
            ("summoned 2", s => s.History.AppendSummoned(s, 2, s.Player)),
            ("energy later round", s => { s.RoundNumber = 2; s.History.AppendEnergySpent(s, 1, s.Player); }),
            ("energy enemy side", s => { s.CurrentSide = CombatSide.Enemy; s.History.AppendEnergySpent(s, 1, s.Player); }),
            ("energy later turn", s => { s.Player.TurnNumber = 2; s.History.AppendEnergySpent(s, 1, s.Player); }),
        };
        var seen = new Dictionary<ulong, string>();
        foreach (var (name, append) in variants)
        {
            var cs = NewCombat();
            AddEnemy(cs, 20);
            AddCard(cs, cs.Player.Hand, StrikeDef);
            AddCard(cs, cs.Player.Hand, GuardDef);
            AttachPower(new KernelPower(), cs.Player, 1);
            append(cs);
            var history = new CanonicalWriter();
            cs.History.WriteCanonical(history);
            ulong h = StateHasher.Hash(history.WrittenSpan);
            Assert.True(seen.TryAdd(h, name), $"the '{name}' entry hashes like '{(seen.TryGetValue(h, out var other) ? other : "?")}'");
        }
        Assert.Equal(variants.Length, seen.Count);
    }

    public static void Test_State_CardPileDeepCloneRefusesOwnedCards()
    {
        var cs = NewCombat();
        var pile = new CardPile(PileType.Discard);
        pile.AddInternal(new CardInstance(StrikeDef, cs.AllocateCardInstanceId()));
        var copy = pile.DeepClone();
        Assert.Equal(1, copy.Count);
        Assert.False(ReferenceEquals(pile[0], copy[0]), "an ownerless pile clones its cards");
        pile.AddInternal(cs.CreateCard(GuardDef, cs.Player));
        Assert.Throws<InvalidOperationException>(() => pile.DeepClone());
        var other = NewPlayer(3);
        var rebound = pile.CloneFor(other);
        Assert.True(ReferenceEquals(other, rebound[1].Owner), "CloneFor rebinds owned cards to the new owner");
        Assert.False(rebound[0].HasOwner, "an ownerless card stays ownerless");
    }

    private static void AppendRandomEntry(CombatState cs, KernelPolicy policy)
    {
        var player = cs.Player;
        var enemy = cs.Monsters[0];
        switch (policy.Next(6))
        {
            case 0:
                cs.History.AppendEnergySpent(cs, policy.Next(4), player);
                break;
            case 1:
                cs.History.AppendCreatureAttacked(cs, enemy, new[] { new DamageResult(player, ValueProp.Move) { UnblockedDamage = policy.Next(7) } });
                break;
            case 2:
                cs.History.AppendMonsterPerformedMove(cs, enemy, new MoveStateDef("STATE_KERNEL_DIGEST", static (_, _, _) => { }), new CreatureState[] { player });
                break;
            case 3:
                cs.History.AppendDamageReceived(cs, player, enemy, new DamageResult(player, ValueProp.Move) { BlockedDamage = policy.Next(3) }, null);
                break;
            case 4:
                cs.History.AppendStarsModified(cs, policy.Next(5) - 2, player);
                break;
            default:
                cs.History.AppendBlockGained(cs, player, policy.Next(9), ValueProp.Move, null);
                break;
        }
    }

    private static void CheckCloneIndependence(CombatState cs, KernelPolicy policy, string label)
    {
        ulong hash = StateHasher.Hash(cs);
        var bytes = StateHasher.CanonicalBytes(cs);
        var clone = cs.DeepClone();
        Assert.Equal(hash, StateHasher.Hash(clone), $"{label}: Hash(Clone(x)) differs from Hash(x)");
        Assert.True(bytes.AsSpan().SequenceEqual(StateHasher.CanonicalBytes(clone)), $"{label}: the clone's canonical bytes differ");
        var cloneOfClone = clone.DeepClone();
        Assert.Equal(hash, StateHasher.Hash(cloneOfClone), $"{label}: Hash(Clone(Clone(x))) differs from Hash(x)");
        var runClone = cs.Run.DeepClone();
        Assert.Equal(hash, StateHasher.Hash(runClone.Combat!), $"{label}: the run clone's combat hash differs");
        int k = 1 + policy.Next(4);
        for (int i = 0; i < k; i++)
        {
            RandomMutation(clone, policy);
        }
        Assert.Equal(hash, StateHasher.Hash(cs), $"{label}: mutating the clone changed the source");
        Assert.True(bytes.AsSpan().SequenceEqual(StateHasher.CanonicalBytes(cs)), $"{label}: mutating the clone changed the source bytes");
        Assert.Equal(hash, StateHasher.Hash(cloneOfClone), $"{label}: mutating the clone changed the clone of the clone");
        Assert.Equal(hash, StateHasher.Hash(runClone.Combat!), $"{label}: mutating the clone changed the run clone");
    }

    private static void RandomMutation(CombatState cs, KernelPolicy policy)
    {
        var player = cs.Player;
        switch (policy.Next(22))
        {
            case 0:
                player.Gold += 1 + policy.Next(9);
                break;
            case 1:
                if (player.Relics.Count > 0 && player.Relics[0] is KernelRelic counter)
                {
                    counter.Counter++;
                    counter.Log.Add(counter.Counter);
                }
                break;
            case 2:
                player.AddRelicInternal(new KernelRelic($"STATE_KERNEL_RELIC_R{policy.Next(5)}"), policy.OneIn(2) ? -1 : 0);
                break;
            case 3:
                {
                    var potion = new KernelPotion($"STATE_KERNEL_POTION_R{policy.Next(4)}") { Charges = policy.Next(3) };
                    var result = player.AddPotionInternal(potion);
                    if (!result.Success && player.Potions.FirstOrDefault() is { } held)
                    {
                        player.DiscardPotionInternal(held);
                    }
                    break;
                }
            case 4:
                if (player.PotionSlots.Length < 6)
                {
                    player.AddToMaxPotionCount(1);
                }
                break;
            case 5:
                AddPet(cs, policy.OneIn(3) ? OstyDef : PetDef, 1 + policy.Next(9));
                break;
            case 6:
                {
                    var creatures = new List<CreatureState>();
                    cs.GetCreatures(creatures);
                    var target = creatures[policy.Next(creatures.Count)];
                    target.Block += policy.Next(4);
                    if (target.CurrentHp > 1)
                    {
                        target.CurrentHp -= 1;
                    }
                    break;
                }
            case 7:
                if (player.Hand.Count > 0)
                {
                    var card = player.Hand[policy.Next(player.Hand.Count)];
                    player.Hand.RemoveInternal(card);
                    player.DiscardPile.AddInternal(card);
                    cs.History.AppendCardDiscarded(cs, card);
                }
                break;
            case 8:
                if (player.DrawPile.Count > 0)
                {
                    var card = player.DrawPile[0];
                    player.DrawPile.RemoveInternal(card);
                    player.Hand.AddInternal(card);
                    cs.History.AppendCardDrawn(cs, card, policy.OneIn(2));
                }
                break;
            case 9:
                if (player.Hand.Count > 0)
                {
                    var target = cs.Monsters.Count > 0 ? cs.Monsters[policy.Next(cs.Monsters.Count)] : null;
                    var play = NewPlay(cs, player.Hand[policy.Next(player.Hand.Count)], target, new ResourceInfo(1, 1, 0, 0));
                    cs.History.AppendCardPlayStarted(cs, play);
                    cs.History.AppendEnergySpent(cs, 1, player);
                    cs.History.AppendBlockGained(cs, player, policy.Next(6), ValueProp.Move, play);
                    cs.History.AppendCardPlayFinished(cs, play);
                }
                break;
            case 10:
                if (cs.Monsters.Count > 0)
                {
                    var monster = cs.Monsters[policy.Next(cs.Monsters.Count)];
                    var result = new DamageResult(player, ValueProp.Move) { BlockedDamage = policy.Next(3), UnblockedDamage = policy.Next(5) };
                    cs.History.AppendDamageReceived(cs, player, monster, result, null);
                    cs.History.AppendCreatureAttacked(cs, monster, new[] { result });
                    cs.History.AppendMonsterPerformedMove(cs, monster, new MoveStateDef("STATE_KERNEL_RANDOM", static (_, _, _) => { }), new CreatureState[] { player });
                }
                break;
            case 11:
                cs.CurrentSide = cs.CurrentSide == CombatSide.Player ? CombatSide.Enemy : CombatSide.Player;
                if (cs.CurrentSide == CombatSide.Player)
                {
                    cs.RoundNumber++;
                    player.IncrementTurnNumber();
                }
                break;
            case 12:
                cs.History.AppendStarsModified(cs, policy.Next(5) - 2, player);
                player.Stars = Math.Max(0, player.Stars + 1);
                break;
            case 13:
                cs.Run.Fidelity.Record(policy.OneIn(2) ? FidelityFlag.ApproximatedRule : FidelityFlag.DeferredListenerPromptInline, $"STATE_KERNEL:Random{policy.Next(3)}");
                break;
            case 14:
                if (cs.Choice.Selectors.Count > 0 && policy.OneIn(2))
                {
                    cs.Choice.PopSelector();
                }
                else
                {
                    cs.Choice.PushSelector(new KernelSelector($"random{policy.Next(3)}"));
                }
                break;
            case 15:
                player.Deck.Add(cs.CreateCard(policy.OneIn(2) ? StrikeDef : GuardDef, player));
                break;
            case 16:
                if (cs.Allies.Count > 1 && cs.Allies[policy.Next(cs.Allies.Count - 1) + 1] is MonsterState pet)
                {
                    pet.Listener = policy.OneIn(2) ? new StatefulListener(pet.CombatId, policy.Next(5)) : new StatelessListener();
                }
                break;
            case 17:
                AttachPower(new KernelPower(), cs.Allies[policy.Next(cs.Allies.Count)], 1 + policy.Next(3));
                break;
            case 18:
                cs.CardOrPotionEffectDepth = policy.Next(3);
                cs.AllocateCardPlayId();
                break;
            case 19:
                if (cs.History.Count > 0 && policy.OneIn(6))
                {
                    cs.History.Clear();
                }
                else
                {
                    var power = player.Powers.Count > 0 ? player.Powers[0] : null;
                    if (power != null)
                    {
                        cs.History.AppendPowerReceived(cs, power, 1 + policy.Next(3), cs.Monsters.Count > 0 ? cs.Monsters[0] : null);
                    }
                }
                break;
            case 20:
                if (player.Relics.Count > 1 && policy.OneIn(2))
                {
                    player.RemoveRelicInternal(player.Relics[player.Relics.Count - 1]);
                }
                else if (player.GetRelic<KernelWaxRelic>() is { IsMelted: false } wax)
                {
                    player.MeltRelicInternal(wax);
                }
                break;
            default:
                cs.PlayersTakingExtraTurn.Add(player.CombatId);
                if (cs.PlayersTakingExtraTurn.Count > 2)
                {
                    cs.PlayersTakingExtraTurn.Clear();
                }
                break;
        }
    }

    private static CombatState BuildRichState(ulong seed)
    {
        var player = NewPlayer();
        var cs = new CombatState(player, Encounter, (int)(seed % 11UL), new RunRngSet(seed + 1000UL));
        cs.IsInProgress = true;
        player.Gold = 99 + (int)(seed % 50UL);
        player.BaseOrbSlotCount = (int)(seed % 3UL);
        for (int i = 0; i < 4; i++)
        {
            player.Deck.Add(cs.CreateCard(i % 2 == 0 ? StrikeDef : GuardDef, player));
        }
        for (int i = 0; i < 5; i++)
        {
            AddCard(cs, player.DrawPile, i % 3 == 0 ? GuardDef : StrikeDef);
        }
        for (int i = 0; i < 3; i++)
        {
            AddCard(cs, player.Hand, i == 1 ? GuardDef : StrikeDef);
        }
        AddCard(cs, player.DiscardPile, GuardDef);
        AddCard(cs, player.ExhaustPile, SlimeDef);
        player.Energy = 2;
        player.Stars = 1;
        player.Phase = PlayerTurnPhase.Play;
        var counter = new KernelRelic("STATE_KERNEL_RELIC_COUNTER") { Counter = 2 };
        counter.Log.Add(1);
        counter.Log.Add(2);
        player.AddRelicInternal(counter);
        player.AddRelicInternal(new KernelRelic("STATE_KERNEL_RELIC_PLAIN"));
        player.AddRelicInternal(new KernelWaxRelic());
        player.AddPotionInternal(new KernelPotion("STATE_KERNEL_POTION_A") { Charges = 1 }, 0);
        player.AddPotionInternal(new KernelPotion("STATE_KERNEL_POTION_B") { Charges = 2 }, player.MaxPotionCount - 1);
        AttachPower(new KernelPower(), player, 2);
        var a = AddEnemy(cs, 30);
        var b = AddEnemy(cs, 24);
        AttachPower(new KernelPower(), a, 1);
        a.Listener = new StatelessListener();
        b.Block = 4;
        var pet = AddPet(cs, OstyDef, 8);
        pet.Listener = new StatefulListener(pet.CombatId, 3);
        AttachPower(new KernelPower(), pet, 1);
        var strike = player.Hand[0];
        var play = NewPlay(cs, strike, a, new ResourceInfo(1, 1, 0, 0));
        cs.History.AppendCardPlayStarted(cs, play);
        cs.History.AppendEnergySpent(cs, 1, player);
        cs.History.AppendDamageReceived(cs, a, player, new DamageResult(a, ValueProp.Move) { UnblockedDamage = 6 }, strike);
        cs.History.AppendCreatureAttacked(cs, player, new[] { new DamageResult(a, ValueProp.Move) { UnblockedDamage = 6 } });
        cs.History.AppendCardPlayFinished(cs, play);
        cs.History.AppendMonsterPerformedMove(cs, b, new MoveStateDef("STATE_KERNEL_CHOMP", static (_, _, _) => { }), new CreatureState[] { player });
        cs.Choice.PushSelector(new KernelSelector("pending"));
        cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, "STATE_KERNEL:Build");
        cs.PlayersTakingExtraTurn.Add(player.CombatId);
        cs.CardOrPotionEffectDepth = 1;
        return cs;
    }

    private static void FillHistory(CombatState cs, int entries, ulong seed)
    {
        var policy = new KernelPolicy(seed);
        var player = cs.Player;
        var enemy = cs.Monsters.Count > 0 ? cs.Monsters[0] : AddEnemy(cs, 50);
        var card = AddCard(cs, player.Hand, StrikeDef);
        var power = new KernelPower();
        AttachPower(power, enemy, 1);
        var move = new MoveStateDef("STATE_KERNEL_FILL", static (_, _, _) => { });
        var history = cs.History;
        while (history.Count < entries)
        {
            switch (history.Count % 10)
            {
                case 0:
                case 1:
                    history.AppendCardDrawn(cs, card, policy.OneIn(2));
                    break;
                case 2:
                    {
                        var play = NewPlay(cs, card, enemy, new ResourceInfo(1, 1, 0, 0));
                        history.AppendCardPlayStarted(cs, play);
                        if (history.Count < entries)
                        {
                            history.AppendCardPlayFinished(cs, play);
                        }
                        break;
                    }
                case 4:
                    history.AppendEnergySpent(cs, 1 + policy.Next(2), player);
                    break;
                case 5:
                    history.AppendDamageReceived(cs, enemy, player, new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = policy.Next(9) }, card);
                    break;
                case 6:
                    history.AppendCreatureAttacked(cs, player, new[] { new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 3 }, new DamageResult(enemy, ValueProp.Move) { UnblockedDamage = 2 } });
                    break;
                case 7:
                    history.AppendMonsterPerformedMove(cs, enemy, move, new CreatureState[] { player });
                    break;
                case 8:
                    history.AppendBlockGained(cs, player, 5, ValueProp.Move, null);
                    break;
                default:
                    history.AppendPowerReceived(cs, power, 1m, player);
                    break;
            }
        }
    }

    private static PlayerState NewPlayer(int combatId = 0) => new(combatId, Character, 60, 80, 3);

    private static CombatState NewCombat()
    {
        var cs = new CombatState(NewPlayer(), Encounter, 0, new RunRngSet(99UL));
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState AddEnemy(CombatState cs, int hp)
    {
        var monster = new MonsterState(cs.AllocateCreatureId(), EnemyDef, hp, hp, null, null);
        cs.AddMonster(monster);
        return monster;
    }

    private static MonsterState AddPet(CombatState cs, MonsterDef def, int hp)
    {
        var pet = new MonsterState(cs.AllocateCreatureId(), def, CombatSide.Player, hp, hp, null, null);
        cs.AddAlly(pet);
        cs.Player.AddPetInternal(pet);
        return pet;
    }

    private static CardInstance AddCard(CombatState cs, CardPile pile, CardDef def)
    {
        var card = cs.CreateCard(def, cs.Player);
        pile.AddInternal(card);
        return card;
    }

    private static CardPlay NewPlay(CombatState cs, CardInstance card, CreatureState? target, ResourceInfo resources)
        => new(cs, card, target) { Resources = resources };

    private static void AttachPower(PowerModel power, CreatureState owner, int amount)
    {
        power.AttachTo(owner);
        power.SetAmount(amount);
        owner.Powers.Add(power);
    }

    private static KernelPotion[] Potions(int n)
    {
        var potions = new KernelPotion[n];
        for (int i = 0; i < n; i++)
        {
            potions[i] = new KernelPotion($"STATE_KERNEL_POTION_{i}");
        }
        return potions;
    }

    private static PlayerState WithSlots(params PotionModel?[] slots)
    {
        var player = NewPlayer();
        player.AddToMaxPotionCount(slots.Length - player.MaxPotionCount);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] is { } potion)
            {
                Assert.True(player.AddPotionInternal(potion, i).Success);
            }
        }
        return player;
    }

    private static byte[] BytesOf(Action<CanonicalWriter> write)
    {
        var w = new CanonicalWriter(16);
        write(w);
        return w.ToArray();
    }

    private static void AssertNoSharedMutable(object original, object clone, string label)
    {
        var problems = new SharedObjectInspector().FindSharedMutable(original, clone);
        Assert.True(problems.Count == 0, $"{label}: the clone shares mutable objects with the source: {string.Join("; ", problems)}");
    }

    private readonly struct NotAnEntry : IHistoryEntry
    {
        public HistoryStamp Stamp => default;
    }

    private sealed class KernelPolicy
    {
        private ulong _state;

        public KernelPolicy(ulong seed)
        {
            _state = seed;
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }
            return (int)(NextULong() % (ulong)maxExclusive);
        }

        public bool OneIn(int n) => Next(n) == 0;

        private ulong NextULong()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }

    private sealed class KernelCardDef : CardDef
    {
        public KernelCardDef(string id, CardType type)
            : base(id, 1, type, CardRarity.Common, type == CardType.Attack ? TargetType.AnyEnemy : TargetType.Self)
        {
        }

        protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] { new DamageVar(6m, ValueProp.Move) };

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }

        public override void AfterCreated(CardInstance card)
        {
            card.BaseReplayCount = AfterCreatedReplayCount;
        }
    }

    private sealed class KernelMonsterDef : MonsterDef
    {
        public KernelMonsterDef(string id)
            : base(id)
        {
        }

        public override int MinInitialHp(CombatState cs) => 10;

        public override int MaxInitialHp(CombatState cs) => 10;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
            => throw new NotSupportedException("Test double has no move state machine");
    }

    private sealed class KernelPower : PowerModel
    {
        public override string Id => "STATE_KERNEL_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;
    }

    private sealed class KeepDeadPower : PowerModel
    {
        public override string Id => "STATE_KERNEL_KEEP_DEAD_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => false;
    }

    private sealed class EtherealPower : PowerModel
    {
        public override string Id => "STATE_KERNEL_ETHEREAL_POWER";

        public override PowerType Type => PowerType.Debuff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override bool TryModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords) => keywords.Add(CardKeyword.Ethereal);
    }

    private class KernelRelic : RelicModel
    {
        private readonly string _id;

        public KernelRelic(string id)
        {
            _id = id;
        }

        public override string Id => _id;

        public override RelicRarity Rarity => RelicRarity.Common;

        public override bool IsStackable => true;

        public int Counter { get; set; }

        public List<int> Log { get; private set; } = new();

        protected override void CopyMutableReferenceState(RelicModel source)
        {
            Log = new List<int>(((KernelRelic)source).Log);
        }

        protected override void WriteExtraCanonical(CanonicalWriter w)
        {
            w.WriteInt32(Counter);
            w.WriteCount(Log.Count);
            for (int i = 0; i < Log.Count; i++)
            {
                w.WriteInt32(Log[i]);
            }
        }
    }

    private sealed class KernelWaxRelic : RelicModel
    {
        public KernelWaxRelic()
        {
            IsWax = true;
        }

        public override string Id => "STATE_KERNEL_WAX_RELIC";

        public override RelicRarity Rarity => RelicRarity.Rare;
    }

    private sealed class KernelPotion : PotionModel
    {
        private readonly string _id;

        public KernelPotion(string id)
        {
            _id = id;
        }

        public override string Id => _id;

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.CombatOnly;

        public override TargetType TargetType => TargetType.Self;

        public int Charges { get; set; }

        protected override void WriteExtraCanonical(CanonicalWriter w) => w.WriteInt32(Charges);
    }

    private sealed class KernelOrb : OrbModel
    {
        public override string Id => "STATE_KERNEL_ORB";

        public int Charge { get; set; }

        public override decimal PassiveVal(CombatState cs) => Charge;

        public override decimal EvokeVal(CombatState cs) => Charge * 2;

        protected override void WriteExtraCanonical(CanonicalWriter w) => w.WriteInt32(Charge);
    }

    private sealed class KernelAffliction : AfflictionModel
    {
        public override string Id => "STATE_KERNEL_AFFLICTION";
    }

    private sealed class KernelSelector : IStatefulChoiceSelector
    {
        private readonly string _label;

        public KernelSelector(string label)
        {
            _label = label;
        }

        public string SelectorId => "STATE_KERNEL_SELECTOR";

        public ChoiceAnswer Answer(ChoicePrompt prompt) => new(Array.Empty<int>(), false);

        public IStatefulChoiceSelector DeepClone() => new KernelSelector(_label);

        public void WriteCanonical(CanonicalWriter w) => w.WriteString(_label);
    }

    private sealed class StatelessListener : AbstractModel
    {
    }

    private sealed class StatefulListener : AbstractModel, ICanonical, IOwnedListener<MonsterState>
    {
        public StatefulListener(int ownerId, int counter)
        {
            OwnerId = ownerId;
            Counter = counter;
        }

        public int OwnerId { get; }

        public int Counter { get; set; }

        public AbstractModel CloneFor(MonsterState newOwner) => new StatefulListener(newOwner.CombatId, Counter);

        public void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(0x7E51);
            w.WriteInt32(OwnerId);
            w.WriteInt32(Counter);
        }
    }

    private sealed class SharedObjectInspector
    {
        private const BindingFlags DeclaredInstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private static readonly Type[] SharedByContract =
        {
            typeof(string),
            typeof(Type),
            typeof(MemberInfo),
            typeof(Assembly),
            typeof(Module),
            typeof(CardDef),
            typeof(MonsterDef),
            typeof(EncounterDef),
            typeof(MonsterMoveStateMachineDef),
            typeof(AiStateDef),
            typeof(AbstractIntent),
        };

        private readonly Dictionary<Type, List<FieldInfo>> _fields = new();
        private readonly Dictionary<object, bool> _immutable = new(ReferenceEqualityComparer.Instance);

        public List<string> FindSharedMutable(object original, object clone)
        {
            var left = Collect(original);
            var right = Collect(clone);
            var problems = new List<string>();
            foreach (var (obj, path) in right)
            {
                if (left.ContainsKey(obj) && !IsDeeplyImmutable(obj, new HashSet<object>(ReferenceEqualityComparer.Instance)))
                {
                    problems.Add($"{obj.GetType().Name} at clone{path} (source{left[obj]})");
                    if (problems.Count >= 10)
                    {
                        break;
                    }
                }
            }
            return problems;
        }

        private Dictionary<object, string> Collect(object root)
        {
            var seen = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
            var pending = new Stack<(object Obj, string Path)>();
            Visit(root, "", seen, pending);
            while (pending.Count > 0)
            {
                var (obj, path) = pending.Pop();
                var type = obj.GetType();
                if (type.IsArray)
                {
                    if (IsLeaf(type.GetElementType()!))
                    {
                        continue;
                    }
                    int index = 0;
                    foreach (var item in (Array)obj)
                    {
                        Visit(item, $"{path}[{index++}]", seen, pending);
                    }
                    continue;
                }
                ExpandFields(obj, type, path, seen, pending);
            }
            return seen;
        }

        private void ExpandFields(object obj, Type type, string path, Dictionary<object, string> seen, Stack<(object Obj, string Path)> pending)
        {
            foreach (var field in FieldsOf(type))
            {
                if (IsLeaf(field.FieldType) || field.FieldType.IsPointer)
                {
                    continue;
                }
                Visit(field.GetValue(obj), $"{path}.{field.Name}", seen, pending);
            }
        }

        private void Visit(object? value, string path, Dictionary<object, string> seen, Stack<(object Obj, string Path)> pending)
        {
            if (value is null)
            {
                return;
            }
            var type = value.GetType();
            if (type.IsValueType)
            {
                if (!IsLeaf(type))
                {
                    ExpandFields(value, type, path, seen, pending);
                }
                return;
            }
            if (IsSharedByContract(type))
            {
                return;
            }
            if (value is Delegate del)
            {
                foreach (var invocation in del.GetInvocationList())
                {
                    Visit(invocation.Target, $"{path}.<target>", seen, pending);
                }
                return;
            }
            if (seen.TryAdd(value, path))
            {
                pending.Push((value, path));
            }
        }

        private bool IsDeeplyImmutable(object obj, HashSet<object> visiting)
        {
            var type = obj.GetType();
            if (IsSharedByContract(type))
            {
                return true;
            }
            if (!type.IsValueType && _immutable.TryGetValue(obj, out var known))
            {
                return known;
            }
            if (!visiting.Add(obj))
            {
                return true;
            }
            bool result;
            if (type.IsArray)
            {
                result = ((Array)obj).Length == 0;
            }
            else if (obj is Delegate del)
            {
                result = del.GetInvocationList().All(d => d.Target is null || IsDeeplyImmutable(d.Target, visiting));
            }
            else
            {
                result = true;
                foreach (var field in FieldsOf(type))
                {
                    if (field.DeclaringType == typeof(AbstractModel) && field.FieldType == typeof(HookMask))
                    {
                        continue;
                    }
                    if (!type.IsValueType && !field.IsInitOnly)
                    {
                        result = false;
                        break;
                    }
                    if (IsLeaf(field.FieldType))
                    {
                        continue;
                    }
                    var value = field.GetValue(obj);
                    if (value != null && !IsDeeplyImmutable(value, visiting))
                    {
                        result = false;
                        break;
                    }
                }
            }
            visiting.Remove(obj);
            if (!type.IsValueType)
            {
                _immutable[obj] = result;
            }
            return result;
        }

        private List<FieldInfo> FieldsOf(Type type)
        {
            if (_fields.TryGetValue(type, out var cached))
            {
                return cached;
            }
            var fields = new List<FieldInfo>();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                fields.AddRange(t.GetFields(DeclaredInstanceFields));
            }
            _fields[type] = fields;
            return fields;
        }

        private static bool IsSharedByContract(Type type)
        {
            foreach (var root in SharedByContract)
            {
                if (root.IsAssignableFrom(type))
                {
                    return true;
                }
            }
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Comparer<>).GetGenericTypeDefinition()
                || typeof(System.Collections.IComparer).IsAssignableFrom(type)
                || (type.GetInterfaces().Any(i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(IEqualityComparer<>) || i.GetGenericTypeDefinition() == typeof(IComparer<>))) && !typeof(System.Collections.ICollection).IsAssignableFrom(type));
        }

        private static bool IsLeaf(Type type)
            => type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(string) || type.IsPointer
               || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && IsLeaf(type.GetGenericArguments()[0]));
    }
}
