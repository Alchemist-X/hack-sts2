using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class CharacterMechanicsTests
{
    private const string Hooks = "hooks";
    private const string HooksAndState = "hooks, state";
    private const string HooksLoopPipelineAndState = "hooks, loop, pipeline, state";
    private const string State = "state";

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("CHARACTER_MECHANICS_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class OstyDouble : MonsterDef
    {
        public OstyDouble()
            : base(Osty.MonsterIdEntry)
        {
        }

        public override int MinInitialHp(CombatState cs) => 1;

        public override int MaxInitialHp(CombatState cs) => 1;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class RecordingOrb : OrbModel
    {
        private readonly string _name;
        private readonly List<string> _log;

        public RecordingOrb(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public override string Id => "CHARACTER_MECHANICS_RECORDING_ORB";

        public Action<CombatState, RecordingOrb>? OnTrigger { get; set; }

        public Action<CombatState, RecordingOrb>? OnEvoke { get; set; }

        public override decimal PassiveVal(CombatState cs) => 0m;

        public override decimal EvokeVal(CombatState cs) => 0m;

        public override void BeforeTurnEndOrbTrigger(CombatState cs)
        {
            _log.Add("end:" + _name);
            OnTrigger?.Invoke(cs, this);
        }

        public override void AfterTurnStartOrbTrigger(CombatState cs)
        {
            _log.Add("start:" + _name);
            OnTrigger?.Invoke(cs, this);
        }

        public override void Passive(CombatState cs, CreatureState? target)
        {
            _log.Add("passive:" + _name + ":" + (target?.CombatId.ToString() ?? "none"));
        }

        public override IEnumerable<CreatureState> Evoke(CombatState cs)
        {
            _log.Add("evoke:" + _name);
            OnEvoke?.Invoke(cs, this);
            return new CreatureState[] { Owner };
        }
    }

    private sealed class ProbePower : PowerModel
    {
        public ProbePower(List<string> log)
        {
            Log = log;
        }

        public List<string> Log { get; }

        public override string Id => "CHARACTER_MECHANICS_PROBE_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public decimal OrbValueBonus { get; set; }

        public int ExtraPassiveTriggers { get; set; }

        public bool AllowStarGain { get; set; } = true;

        public bool PayExcessEnergyWithStars { get; set; }

        public decimal? SummonAmountOverride { get; set; }

        public bool RemoveCreaturesAfterDeath { get; set; } = true;

        public override decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal value) => value + OrbValueBonus;

        public override int ModifyOrbPassiveTriggerCounts(CombatState cs, OrbModel orb, int triggerCount) => triggerCount + ExtraPassiveTriggers;

        public override void AfterModifyingOrbPassiveTriggerCount(CombatState cs, OrbModel orb) => Log.Add("afterModifyingTriggerCount:" + orb.Id);

        public override void AfterOrbChanneled(CombatState cs, PlayerState player, OrbModel orb)
            => Log.Add($"channeled:{orb.Id}:queued={player.OrbQueue.Orbs.Contains(orb)}");

        public override void AfterOrbEvoked(CombatState cs, OrbModel orb, IEnumerable<CreatureState> targets)
            => Log.Add($"evoked:{orb.Id}:targets={string.Join(",", targets.Select(t => t.CombatId))}:removed={orb.HasBeenRemovedFromState}");

        public override bool ShouldGainStars(CombatState cs, decimal amount, PlayerState player)
        {
            Log.Add("shouldGainStars:" + amount);
            return AllowStarGain;
        }

        public override void AfterStarsGained(CombatState cs, int amount, PlayerState gainer) => Log.Add("starsGained:" + amount);

        public override void AfterStarsSpent(CombatState cs, int amount, PlayerState spender) => Log.Add("starsSpent:" + amount);

        public override bool ShouldPayExcessEnergyCostWithStars(CombatState cs, PlayerState player) => PayExcessEnergyWithStars;

        public override decimal ModifySummonAmount(CombatState cs, PlayerState summoner, decimal amount, AbstractModel? source) => SummonAmountOverride ?? amount;

        public override void AfterSummon(CombatState cs, PlayerState summoner, decimal amount) => Log.Add("summon:" + amount);

        public override void AfterOstyRevived(CombatState cs, CreatureState osty) => Log.Add("revived:" + osty.CombatId);

        public override void AfterForge(CombatState cs, decimal amount, PlayerState forger, AbstractModel? source)
            => Log.Add($"forge:{amount}:source={(source == null ? "none" : "set")}");

        public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => RemoveCreaturesAfterDeath;
    }

    private sealed class HibernateDouble : PowerModel
    {
        public override string Id => FrostOrb.HibernatePowerId;

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;
    }

    private sealed class BladeDouble : CardDef, ISovereignBladeDef
    {
        public BladeDouble()
            : base(ForgeCmd.SovereignBladeId, 2, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy)
        {
        }

        protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] { new DamageVar(10m, ValueProp.Move) };

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }

        public void AddDamage(CardInstance card, decimal amount)
        {
            card.Vars.Damage.BaseValue += amount;
        }
    }

    private sealed class SeamlessBladeDouble : CardDef
    {
        public SeamlessBladeDouble()
            : base(ForgeCmd.SovereignBladeId, 2, CardType.Attack, CardRarity.Token, TargetType.AnyEnemy)
        {
        }

        protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] { new DamageVar(10m, ValueProp.Move) };

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class OtherCardDouble : CardDef
    {
        public OtherCardDouble()
            : base("CHARACTER_MECHANICS_OTHER_CARD", 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
        {
        }

        protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] { new DamageVar(6m, ValueProp.Move) };

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private static CombatState NewCombat(int monsters = 1, int monsterHp = 50, int playerHp = 80, int baseOrbSlots = 0)
    {
        var player = new PlayerState(0, "DEFECT", playerHp, playerHp, 3) { BaseOrbSlotCount = baseOrbSlots, Energy = 3 };
        var cs = new CombatState(player, "CHARACTER_MECHANICS_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        var def = new MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, monsterHp, monsterHp, null, null));
        }
        return cs;
    }

    private static ProbePower AddProbe(CreatureState owner, List<string>? log = null)
    {
        var probe = new ProbePower(log ?? new List<string>());
        probe.ApplyInternal(owner, 1);
        return probe;
    }

    private static T Orb<T>(PlayerState owner) where T : OrbModel, new()
    {
        var orb = new T();
        orb.AttachTo(owner);
        return orb;
    }

    private static T Queued<T>(PlayerState owner) where T : OrbModel, new()
    {
        var orb = Orb<T>(owner);
        Assert.True(owner.OrbQueue.TryEnqueue(orb), "test setup enqueues into a free slot");
        return orb;
    }

    private static void MakeEnding(CombatState cs)
    {
        cs.Outcome = CombatOutcome.Defeat;
        Assert.True(CombatStatus.IsEnding(cs), "a pending loss is the ending window");
    }

    private static void MakeOver(CombatState cs)
    {
        cs.IsInProgress = false;
        cs.Outcome = CombatOutcome.Victory;
        Assert.True(CombatStatus.IsOverOrEnding(cs), "a finished combat is over");
        Assert.False(CombatStatus.IsEnding(cs), "a finished combat is not in the ending window");
    }

    private static MonsterState AddOsty(CombatState cs, int currentHp, int maxHp, bool inPetList = true, MonsterDef? def = null)
    {
        var osty = new MonsterState(cs.AllocateCreatureId(), def ?? MonsterRegistry.Get(Osty.MonsterIdEntry), currentHp, maxHp, null, null)
        {
            Side = CombatSide.Player
        };
        cs.AddAlly(osty);
        if (inPetList)
        {
            PlayerPets.AddPetInternal(cs.Player, osty);
        }
        else
        {
            osty.PetOwnerId = cs.Player.CombatId;
        }
        return osty;
    }

    private static CardInstance AddCard(CombatState cs, CardDef def, CardPile pile, bool dupe = false)
    {
        var card = new CardInstance(def, cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        card.IsDupe = dupe;
        pile.AddInternal(card);
        return card;
    }

    private static ulong Hash(CombatState cs) => StateHasher.Hash(cs);

    private static void AssertCloneHashesEqualAndIsolated(CombatState cs, Action<CombatState> mutate, string label)
    {
        ulong before = Hash(cs);
        var clone = cs.DeepClone();
        Assert.Equal(before, Hash(clone), label + ": Hash(Clone(x)) == Hash(x)");
        mutate(clone);
        Assert.NotEqual(before, Hash(clone), label + ": the mutation changes the clone's hash");
        Assert.Equal(before, Hash(cs), label + ": mutating the clone leaves the source unchanged");
    }

    public static void Test_CharacterMechanics_OrbIdsAreTheSlugsOfTheGameClassNames()
    {
        var expected = new (string Id, Type Type)[]
        {
            (LightningOrb.OrbIdEntry, typeof(LightningOrb)),
            (FrostOrb.OrbIdEntry, typeof(FrostOrb)),
            (DarkOrb.OrbIdEntry, typeof(DarkOrb)),
            (PlasmaOrb.OrbIdEntry, typeof(PlasmaOrb)),
            (GlassOrb.OrbIdEntry, typeof(GlassOrb))
        };
        foreach (var (id, type) in expected)
        {
            Assert.Equal(DeterministicHash.Slugify(type.Name), id, type.Name);
            Assert.True(OrbRegistry.Models.Contains(id), id + " is registered");
            Assert.Equal(type, OrbRegistry.TypeOf(id));
            var orb = OrbRegistry.Create(id);
            Assert.Equal(id, orb.Id);
            Assert.False(orb.HasOwner, id + " is created without an owner");
            Assert.False(orb.HasBeenRemovedFromState, id + " starts in the state");
            Assert.False(ReferenceEquals(orb, OrbRegistry.Create(id)), id + ": every Create is a fresh instance");
        }
        Assert.Equal("OSTY", DeterministicHash.Slugify(nameof(Osty)));
        Assert.True(MonsterRegistry.Get(Osty.MonsterIdEntry) is Osty, "OSTY is the Osty def");
    }

    public static void Test_CharacterMechanics_GetRandomOrbDrawsTheGameOrderFromTheGivenStream()
    {
        Assert.SequenceEqual(new[] { "LIGHTNING_ORB", "FROST_ORB", "DARK_ORB", "PLASMA_ORB", "GLASS_ORB" }, OrbModel.ValidOrbIds);
        Assert.SequenceEqual(new[] { "DARK_ORB", "FROST_ORB", "GLASS_ORB", "LIGHTNING_ORB", "PLASMA_ORB" }, OrbRegistry.AllIds.Where(OrbModel.ValidOrbIds.Contains),
            "the registry lists ids in ordinal order, which is not the game's draw order");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var rng = new Rng(seed, "combat_orbs");
            for (int draw = 0; draw < 3; draw++)
            {
                var predictor = rng.DeepClone();
                string expected = OrbModel.ValidOrbIds[predictor.NextInt(0, OrbModel.ValidOrbIds.Count)];
                int counter = rng.Counter;
                var orb = OrbModel.GetRandomOrb(rng);
                Assert.Equal(expected, orb.Id, $"seed {seed} draw {draw}");
                Assert.Equal(counter + 1, rng.Counter, "one NextInt per draw");
                Assert.Equal(OrbRegistry.TypeOf(expected), orb.GetType());
                Assert.False(orb.HasOwner, "the drawn orb is a fresh, unowned instance");
                seen.Add(orb.Id);
            }
        }
        Assert.Equal(5, seen.Count, "all five orbs are reachable");

        var run = new RunRngSet(77UL);
        var before = Enum.GetValues<RunRngType>().ToDictionary(t => t, t => run.Get(t).Counter);
        OrbModel.GetRandomOrb(run.CombatOrbGeneration);
        foreach (var type in Enum.GetValues<RunRngType>())
        {
            Assert.Equal(before[type] + (type == RunRngType.CombatOrbs ? 1 : 0), run.Get(type).Counter, type + " counter");
        }
    }

    public static void Test_CharacterMechanics_OstyIsAOneHpMonsterWithOnlyNothingMove()
    {
        var cs = NewCombat();
        var def = MonsterRegistry.Get(Osty.MonsterIdEntry);
        Assert.Equal(1, def.MinInitialHp(cs));
        Assert.Equal(1, def.MaxInitialHp(cs));
        var high = new CombatState(new PlayerState(0, "NECROBINDER", 70, 70, 3), "CHARACTER_MECHANICS_TEST", 10, new RunRngSet(new Rng(5UL)));
        Assert.Equal(1, def.MinInitialHp(high), "no ascension changes Osty's HP");
        Assert.Equal(1, def.MaxInitialHp(high));
        var self = new MonsterState(cs.AllocateCreatureId(), def, 1, 1, def.CreateExtraState(), null);
        var machine = def.GenerateMoveStateMachine(cs, self);
        Assert.Equal(Osty.NothingMoveId, machine.InitialStateId);
        Assert.Equal(1, machine.States.Count, "a single state");
        var nothing = (MoveStateDef)machine.States[Osty.NothingMoveId];
        Assert.Equal(0, nothing.Intents.Count, "NOTHING_MOVE has no intent");
        Assert.False(nothing.IntendsToAttack);
        Assert.True(ReferenceEquals(nothing, nothing.FollowUpState), "NOTHING_MOVE follows itself");
        Assert.Equal(Osty.NothingMoveId, nothing.GetNextState(cs, self, cs.Rng.MonsterAi));
        ulong hash = Hash(cs);
        nothing.Perform(cs, self, Array.Empty<CreatureState>());
        Assert.Equal(hash, Hash(cs), "performing NOTHING_MOVE changes nothing");
    }

    public static void Test_CharacterMechanics_OrbQueueEnqueueRespectsCapacity()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var queue = player.OrbQueue;
        Assert.Equal(0, queue.Capacity, "a new queue has no slot");
        var first = Orb<PlasmaOrb>(player);
        Assert.False(queue.TryEnqueue(first), "no capacity: TryEnqueue declines");
        Assert.Equal(0, queue.Orbs.Count);
        queue.AddCapacity(2);
        Assert.True(queue.TryEnqueue(first));
        var second = Orb<DarkOrb>(player);
        Assert.True(queue.TryEnqueue(second));
        Assert.SequenceEqual(new OrbModel[] { first, second }, queue.Orbs, "enqueue appends at the back");
        var third = Orb<FrostOrb>(player);
        var full = Assert.Throws<InvalidOperationException>(() => queue.TryEnqueue(third));
        Assert.Equal("OrbQueue is full", full.Message);
        Assert.Equal(2, queue.Orbs.Count, "a refused enqueue changes nothing");
        Assert.Equal(10, OrbQueue.MaxCapacity, "the constant exists but the queue never applies it");
    }

    public static void Test_CharacterMechanics_OrbQueueAddCapacityHasNoCap()
    {
        var queue = new OrbQueue();
        queue.AddCapacity(7);
        queue.AddCapacity(7);
        Assert.Equal(14, queue.Capacity, "the queue itself never caps at MaxCapacity");
        queue.AddCapacity(-3);
        Assert.Equal(11, queue.Capacity, "AddCapacity adds whatever it is given");
    }

    public static void Test_CharacterMechanics_OrbQueueRemoveCapacityEvictsFromTheBackWithoutRemoveInternal()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var queue = player.OrbQueue;
        queue.AddCapacity(3);
        var a = Queued<PlasmaOrb>(player);
        var b = Queued<DarkOrb>(player);
        var c = Queued<FrostOrb>(player);
        queue.RemoveCapacity(2);
        Assert.Equal(1, queue.Capacity);
        Assert.SequenceEqual(new OrbModel[] { a }, queue.Orbs, "orbs leave from the back");
        Assert.False(b.HasBeenRemovedFromState, "eviction does not mark the orb removed");
        Assert.False(c.HasBeenRemovedFromState);
        queue.RemoveCapacity(5);
        Assert.Equal(0, queue.Capacity, "capacity floors at 0");
        Assert.Equal(0, queue.Orbs.Count);
        Assert.False(a.HasBeenRemovedFromState);
    }

    public static void Test_CharacterMechanics_OrbQueueInsertChecksTheIndexAgainstCapacity()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var queue = player.OrbQueue;
        queue.AddCapacity(4);
        var a = Queued<PlasmaOrb>(player);
        var front = Orb<DarkOrb>(player);
        queue.Insert(0, front);
        Assert.SequenceEqual(new OrbModel[] { front, a }, queue.Orbs);
        var tooFar = Assert.Throws<InvalidOperationException>(() => queue.Insert(4, Orb<FrostOrb>(player)));
        Assert.Equal("idx cannot be greater than capacity", tooFar.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Insert(3, Orb<GlassOrb>(player)), "an index inside the capacity but past the end of the list fails in List.Insert");
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.Insert(-1, Orb<GlassOrb>(player)), "a negative index passes the capacity check and fails in List.Insert");
        Assert.Equal(2, queue.Orbs.Count);
        var last = Orb<LightningOrb>(player);
        queue.Insert(2, last);
        Assert.SequenceEqual(new OrbModel[] { front, a, last }, queue.Orbs, "an index equal to the count appends");
        queue.Insert(0, Orb<FrostOrb>(player));
        var over = Orb<FrostOrb>(player);
        queue.Insert(0, over);
        Assert.Equal(5, queue.Orbs.Count, "Insert does not check that the queue is full");
        Assert.True(ReferenceEquals(over, queue.Orbs[0]));
    }

    public static void Test_CharacterMechanics_OrbQueueRemoveClearAndResetForNewCombat()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var queue = player.OrbQueue;
        queue.AddCapacity(3);
        var a = Queued<PlasmaOrb>(player);
        var b = Queued<PlasmaOrb>(player);
        Assert.True(queue.Remove(a), "Remove finds the orb by reference");
        Assert.False(queue.Remove(a), "a second Remove finds nothing");
        Assert.False(queue.Remove(Orb<PlasmaOrb>(player)), "another instance of the same orb is not in the queue");
        Assert.SequenceEqual(new OrbModel[] { b }, queue.Orbs);
        Assert.False(a.HasBeenRemovedFromState, "Remove alone does not mark the orb removed");
        queue.Clear();
        Assert.Equal(0, queue.Capacity, "Clear also drops every slot");
        Assert.Equal(0, queue.Orbs.Count);
        queue.AddCapacity(5);
        Queued<DarkOrb>(player);
        queue.ResetForNewCombat(3);
        Assert.Equal(3, queue.Capacity, "a new combat starts with the base slot count");
        Assert.Equal(0, queue.Orbs.Count);
    }

    public static void Test_CharacterMechanics_OrbTurnTriggersRunOverASnapshotInQueueOrder()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var queue = player.OrbQueue;
        var log = new List<string>();
        queue.BeforeTurnEnd(cs);
        queue.AfterTurnStart(cs);
        Assert.Equal(0, log.Count, "an empty queue triggers nothing");
        queue.AddCapacity(4);
        var a = new RecordingOrb("a", log);
        var b = new RecordingOrb("b", log);
        var c = new RecordingOrb("c", log);
        foreach (var orb in new[] { a, b, c })
        {
            orb.AttachTo(player);
            queue.TryEnqueue(orb);
        }
        var late = new RecordingOrb("late", log);
        late.AttachTo(player);
        a.OnTrigger = (_, self) =>
        {
            queue.Remove(b);
            queue.TryEnqueue(late);
        };
        queue.BeforeTurnEnd(cs);
        Assert.SequenceEqual(new[] { "end:a", "end:b", "end:c" }, log, "the trigger loop walks the orbs that were queued when it started");
        Assert.SequenceEqual(new OrbModel[] { a, c, late }, queue.Orbs);
        log.Clear();
        a.OnTrigger = null;
        queue.AfterTurnStart(cs);
        Assert.SequenceEqual(new[] { "start:a", "start:c", "start:late" }, log);
    }

    public static void Test_CharacterMechanics_OrbTurnTriggersSkipAQueueWhoseOwnerIsNotInTheCombat()
    {
        var cs = NewCombat();
        var stranger = new PlayerState(50, "DEFECT", 70, 70, 3);
        var log = new List<string>();
        stranger.OrbQueue.AddCapacity(2);
        var orb = new RecordingOrb("x", log);
        orb.AttachTo(stranger);
        stranger.OrbQueue.TryEnqueue(orb);
        stranger.OrbQueue.BeforeTurnEnd(cs);
        stranger.OrbQueue.AfterTurnStart(cs);
        Assert.Equal(0, log.Count, "the owner's creature has no combat, so the loop exits before the first orb");
        var other = NewCombat();
        other.Player.OrbQueue.AddCapacity(1);
        var mine = new RecordingOrb("mine", log);
        mine.AttachTo(other.Player);
        other.Player.OrbQueue.TryEnqueue(mine);
        other.Player.OrbQueue.BeforeTurnEnd(cs);
        Assert.Equal(0, log.Count, "a queue is checked against the combat it is triggered in");
        other.Player.OrbQueue.BeforeTurnEnd(other);
        Assert.SequenceEqual(new[] { "end:mine" }, log);
    }

    public static void Test_CharacterMechanics_PlasmaValuesBypassModifyOrbValueAndGrantEnergy()
    {
        var cs = NewCombat();
        var player = cs.Player;
        player.Energy = 0;
        var plasma = Orb<PlasmaOrb>(player);
        Assert.Equal(1m, plasma.PassiveVal(cs));
        Assert.Equal(2m, plasma.EvokeVal(cs));
        plasma.Passive(cs, null);
        Assert.Equal(1, player.Energy, "the passive gains 1 energy");
        var targets = plasma.Evoke(cs).ToList();
        Assert.Equal(3, player.Energy, "the evoke gains 2 energy");
        Assert.SequenceEqual(new CreatureState[] { player }, targets, "the evoke targets its owner");
    }

    public static void Test_CharacterMechanics_OrbsThatCannotTargetThrowOnATarget()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var monster = cs.Monsters[0];
        Assert.Equal("Frost orbs cannot target creatures.", Assert.Throws<InvalidOperationException>(() => Orb<FrostOrb>(player).Passive(cs, monster)).Message);
        Assert.Equal("Dark orbs cannot target creatures.", Assert.Throws<InvalidOperationException>(() => Orb<DarkOrb>(player).Passive(cs, monster)).Message);
        Assert.Equal("Plasma orbs cannot target creatures.", Assert.Throws<InvalidOperationException>(() => Orb<PlasmaOrb>(player).Passive(cs, monster)).Message);
        Assert.Equal(0, player.Block);
        Assert.Equal(3, player.Energy);
    }

    public static void Test_CharacterMechanics_DarkEvokeHitsTheFirstLowestHpHittableEnemy()
    {
        var cs = NewCombat(monsters: 4, monsterHp: 30);
        var player = cs.Player;
        PowerCmd.Apply<StrengthPower>(cs, player, 5m, player, null);
        cs.Monsters[0].CurrentHp = 20;
        cs.Monsters[1].CurrentHp = 9;
        cs.Monsters[2].CurrentHp = 9;
        cs.Monsters[3].CurrentHp = 0;
        var dark = Orb<DarkOrb>(player);
        Assert.Equal(6m, dark.EvokeVal(cs), "the evoke value starts at 6 and bypasses ModifyOrbValue");
        var targets = dark.Evoke(cs).ToList();
        Assert.SequenceEqual(new CreatureState[] { cs.Monsters[1] }, targets, "MinBy keeps the first of equal HP; the dead enemy is not hittable");
        Assert.Equal(3, cs.Monsters[1].CurrentHp, "6 unpowered damage, Strength does not apply");
        Assert.Equal(9, cs.Monsters[2].CurrentHp);
        Assert.Equal(20, cs.Monsters[0].CurrentHp);

        var empty = NewCombat(monsters: 1);
        empty.Monsters[0].CurrentHp = 0;
        var lonely = Orb<DarkOrb>(empty.Player);
        Assert.Equal(0, lonely.Evoke(empty).Count(), "no hittable enemy: no target");
    }

    public static void Test_CharacterMechanics_OrbBaseValuesWithoutModifiers()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_OrbBaseValuesWithoutModifiers), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            Assert.Equal(1m, Orb<PlasmaOrb>(player).PassiveVal(cs));
            Assert.Equal(6m, Orb<DarkOrb>(player).EvokeVal(cs));
            Assert.Equal(3m, Orb<LightningOrb>(player).PassiveVal(cs));
            Assert.Equal(8m, Orb<LightningOrb>(player).EvokeVal(cs));
            Assert.Equal(2m, Orb<FrostOrb>(player).PassiveVal(cs));
            Assert.Equal(5m, Orb<FrostOrb>(player).EvokeVal(cs));
            Assert.Equal(6m, Orb<DarkOrb>(player).PassiveVal(cs));
            Assert.Equal(4m, Orb<GlassOrb>(player).PassiveVal(cs));
            Assert.Equal(8m, Orb<GlassOrb>(player).EvokeVal(cs));
        });
    }

    public static void Test_CharacterMechanics_ModifyOrbValueAppliesToEveryValueButPlasmaAndDarkEvoke()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_ModifyOrbValueAppliesToEveryValueButPlasmaAndDarkEvoke), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            AddProbe(player).OrbValueBonus = 2m;
            Assert.Equal(5m, Orb<LightningOrb>(player).PassiveVal(cs));
            Assert.Equal(10m, Orb<LightningOrb>(player).EvokeVal(cs));
            Assert.Equal(4m, Orb<FrostOrb>(player).PassiveVal(cs));
            Assert.Equal(7m, Orb<FrostOrb>(player).EvokeVal(cs));
            Assert.Equal(8m, Orb<DarkOrb>(player).PassiveVal(cs));
            Assert.Equal(6m, Orb<DarkOrb>(player).EvokeVal(cs), "Dark's evoke value is its own field");
            Assert.Equal(1m, Orb<PlasmaOrb>(player).PassiveVal(cs), "Plasma's values are constants");
            Assert.Equal(2m, Orb<PlasmaOrb>(player).EvokeVal(cs));
            Assert.Equal(6m, Orb<GlassOrb>(player).PassiveVal(cs));
            Assert.Equal(12m, Orb<GlassOrb>(player).EvokeVal(cs), "Glass evokes twice its modified passive");
            var dark = Orb<DarkOrb>(player);
            dark.Passive(cs, null);
            Assert.Equal(14m, dark.EvokeVal(cs), "the passive adds its modified value to the evoke value");
        });
    }

    public static void Test_CharacterMechanics_LightningDamagesARandomHittableOpponentOrTheGivenTarget()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_LightningDamagesARandomHittableOpponentOrTheGivenTarget), Hooks, () =>
        {
            var cs = NewCombat(monsters: 3, monsterHp: 40);
            var player = cs.Player;
            PowerCmd.Apply<StrengthPower>(cs, player, 4m, player, null);
            cs.Monsters[1].CurrentHp = 0;
            var lightning = Orb<LightningOrb>(player);
            var hittable = new[] { cs.Monsters[0], cs.Monsters[2] };
            var predictor = cs.Rng.CombatTargets.DeepClone();
            var expected = hittable[predictor.NextInt(0, hittable.Length)];
            int counter = cs.Rng.CombatTargets.Counter;
            var targets = lightning.Evoke(cs).ToList();
            Assert.SequenceEqual(new CreatureState[] { expected }, targets);
            Assert.Equal(counter + 1, cs.Rng.CombatTargets.Counter, "one CombatTargets draw");
            Assert.Equal(32, expected.CurrentHp, "8 unpowered damage");

            counter = cs.Rng.CombatTargets.Counter;
            lightning.Passive(cs, cs.Monsters[2]);
            Assert.Equal(counter, cs.Rng.CombatTargets.Counter, "an explicit target draws nothing");
            Assert.Equal(ReferenceEquals(expected, cs.Monsters[2]) ? 29 : 37, cs.Monsters[2].CurrentHp, "3 unpowered damage to the given target");

            cs.Monsters[0].CurrentHp = 0;
            cs.Monsters[2].CurrentHp = 0;
            counter = cs.Rng.CombatTargets.Counter;
            Assert.Equal(0, lightning.Evoke(cs).Count(), "no hittable opponent: nothing");
            Assert.Equal(counter, cs.Rng.CombatTargets.Counter, "and no draw");
            lightning.Passive(cs, cs.Monsters[1]);
            Assert.Equal(0, cs.Monsters[1].CurrentHp, "a dead target is not hit even when given");
        });
    }

    public static void Test_CharacterMechanics_FrostGainsUnpoweredBlockForItsOwner()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_FrostGainsUnpoweredBlockForItsOwner), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            PowerCmd.Apply<DexterityPower>(cs, player, 3m, player, null);
            var frost = Orb<FrostOrb>(player);
            frost.Passive(cs, null);
            Assert.Equal(2, player.Block, "2 unpowered block, Dexterity does not apply");
            var targets = frost.Evoke(cs).ToList();
            Assert.Equal(7, player.Block, "5 more block");
            Assert.SequenceEqual(new CreatureState[] { player }, targets);
            new HibernateDouble().ApplyInternal(player, 1);
            frost.Passive(cs, null);
            Assert.Equal(9, player.Block, "with Hibernate only the other players gain extra block; single player has none");
            Assert.SequenceEqual(new CreatureState[] { player }, frost.Evoke(cs).ToList(), "with Hibernate the evoke targets every player creature");
            Assert.Equal(14, player.Block);
        });
    }

    public static void Test_CharacterMechanics_DarkPassiveAddsItsPassiveValueToItsEvokeValue()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_DarkPassiveAddsItsPassiveValueToItsEvokeValue), Hooks, () =>
        {
            var cs = NewCombat(monsters: 1, monsterHp: 40);
            var dark = Orb<DarkOrb>(cs.Player);
            dark.Passive(cs, null);
            dark.Passive(cs, null);
            Assert.Equal(18m, dark.EvokeVal(cs), "6 + 6 + 6");
            dark.Evoke(cs);
            Assert.Equal(22, cs.Monsters[0].CurrentHp);
        });
    }

    public static void Test_CharacterMechanics_GlassPassiveDecaysByOneAndEvokeIsTwiceThePassive()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_GlassPassiveDecaysByOneAndEvokeIsTwiceThePassive), Hooks, () =>
        {
            var cs = NewCombat(monsters: 3, monsterHp: 40);
            cs.Monsters[2].CurrentHp = 0;
            var glass = Orb<GlassOrb>(cs.Player);
            var hp = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                glass.Passive(cs, cs.Monsters[0]);
                hp.Add(cs.Monsters[0].CurrentHp);
            }
            Assert.SequenceEqual(new[] { 36, 33, 31, 30, 30, 30 }, hp, "4, 3, 2, 1, then nothing once the value reaches 0");
            Assert.Equal(30, cs.Monsters[1].CurrentHp, "the passive hits every hittable enemy whatever the target");
            Assert.Equal(0m, glass.PassiveVal(cs));
            Assert.Equal(0, glass.Evoke(cs).Count(), "a spent Glass orb evokes nothing");
            Assert.Equal(30, cs.Monsters[0].CurrentHp);

            var fresh = Orb<GlassOrb>(cs.Player);
            var targets = fresh.Evoke(cs).ToList();
            Assert.SequenceEqual(new CreatureState[] { cs.Monsters[0], cs.Monsters[1] }, targets);
            Assert.Equal(22, cs.Monsters[0].CurrentHp, "evoke = 2 x passive = 8");
            Assert.Equal(22, cs.Monsters[1].CurrentHp);
            Assert.Equal(4m, fresh.PassiveVal(cs), "the evoke does not decay the passive");
        });
    }

    public static void Test_CharacterMechanics_TriggerPassiveRepeatsPassiveForTheModifiedCount()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_TriggerPassiveRepeatsPassiveForTheModifiedCount), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            player.Energy = 0;
            var log = new List<string>();
            var probe = AddProbe(player, log);
            var plasma = Orb<PlasmaOrb>(player);
            plasma.TriggerPassive(cs, null);
            Assert.Equal(1, player.Energy, "no modifier: one passive");
            Assert.Equal(0, log.Count, "an unmodified count calls no AfterModifying listener");
            probe.ExtraPassiveTriggers = 2;
            plasma.TriggerPassive(cs, null);
            Assert.Equal(4, player.Energy, "three passives");
            Assert.SequenceEqual(new[] { "afterModifyingTriggerCount:" + PlasmaOrb.OrbIdEntry }, log);
            log.Clear();
            player.OrbQueue.AddCapacity(1);
            player.OrbQueue.TryEnqueue(plasma);
            player.OrbQueue.AfterTurnStart(cs);
            Assert.Equal(7, player.Energy, "Plasma's turn-start trigger goes through TriggerPassive");
            player.OrbQueue.BeforeTurnEnd(cs);
            Assert.Equal(7, player.Energy, "Plasma has no turn-end trigger");
        });
    }

    public static void Test_CharacterMechanics_OrbCmdPassiveRunsPassiveOnceByDefault()
    {
        var cs = NewCombat();
        var player = cs.Player;
        player.Energy = 0;
        var plasma = Orb<PlasmaOrb>(player);
        OrbCmd.Passive(cs, plasma, null);
        Assert.Equal(1, player.Energy, "countAffectedByHooks defaults to false: one passive, no hook");
        var log = new List<string>();
        var recording = new RecordingOrb("r", log);
        recording.AttachTo(player);
        OrbCmd.Passive(cs, recording, cs.Monsters[0]);
        Assert.SequenceEqual(new[] { "passive:r:" + cs.Monsters[0].CombatId }, log, "the target is passed through");
        MakeEnding(cs);
        OrbCmd.Passive(cs, plasma, null);
        OrbCmd.Passive(cs, plasma, null, countAffectedByHooks: true);
        Assert.Equal(1, player.Energy, "nothing once the combat is ending");
    }

    public static void Test_CharacterMechanics_OrbCmdPassiveCountedByHooksUsesTriggerPassive()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_OrbCmdPassiveCountedByHooksUsesTriggerPassive), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            player.Energy = 0;
            AddProbe(player).ExtraPassiveTriggers = 1;
            var plasma = Orb<PlasmaOrb>(player);
            OrbCmd.Passive(cs, plasma, null, countAffectedByHooks: true);
            Assert.Equal(2, player.Energy);
            OrbCmd.Passive(cs, plasma, null);
            Assert.Equal(3, player.Energy, "without the flag the count modifier is ignored");
        });
    }

    public static void Test_CharacterMechanics_AddSlotsIsCappedAtTenSlots()
    {
        var cs = NewCombat();
        var player = cs.Player;
        OrbCmd.AddSlots(cs, player, 3);
        Assert.Equal(3, player.OrbQueue.Capacity);
        OrbCmd.AddSlots(cs, player, 5);
        Assert.Equal(8, player.OrbQueue.Capacity);
        OrbCmd.AddSlots(cs, player, 5);
        Assert.Equal(10, player.OrbQueue.Capacity, "Math.Min(10 - Capacity, amount)");
        OrbCmd.AddSlots(cs, player, 1);
        Assert.Equal(10, player.OrbQueue.Capacity);
        player.OrbQueue.AddCapacity(2);
        OrbCmd.AddSlots(cs, player, 1);
        Assert.Equal(10, player.OrbQueue.Capacity, "above the cap the amount turns negative and removes the excess slots");
    }

    public static void Test_CharacterMechanics_RemoveSlotsClampsToTheCapacity()
    {
        var cs = NewCombat();
        var player = cs.Player;
        OrbCmd.AddSlots(cs, player, 3);
        var a = Queued<PlasmaOrb>(player);
        var b = Queued<PlasmaOrb>(player);
        var c = Queued<PlasmaOrb>(player);
        OrbCmd.RemoveSlots(cs, player, 1);
        Assert.Equal(2, player.OrbQueue.Capacity);
        Assert.SequenceEqual(new OrbModel[] { a, b }, player.OrbQueue.Orbs, "the last orb leaves without an evoke");
        Assert.False(c.HasBeenRemovedFromState);
        OrbCmd.RemoveSlots(cs, player, 15);
        Assert.Equal(0, player.OrbQueue.Capacity);
        Assert.Equal(0, player.OrbQueue.Orbs.Count);
        Assert.Equal(3, player.Energy, "removed orbs are not evoked");
    }

    public static void Test_CharacterMechanics_SlotCommandsAreNoOpsOnceTheCombatIsOverOrEnding()
    {
        var ending = NewCombat();
        MakeEnding(ending);
        OrbCmd.AddSlots(ending, ending.Player, 2);
        Assert.Equal(0, ending.Player.OrbQueue.Capacity);
        var over = NewCombat();
        over.Player.OrbQueue.AddCapacity(2);
        MakeOver(over);
        OrbCmd.RemoveSlots(over, over.Player, 2);
        Assert.Equal(2, over.Player.OrbQueue.Capacity);
        OrbCmd.AddSlots(over, over.Player, 2);
        Assert.Equal(2, over.Player.OrbQueue.Capacity);
    }

    public static void Test_CharacterMechanics_ChannelOpensASlotForAZeroSlotCharacter()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_ChannelOpensASlotForAZeroSlotCharacter), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            var plasma = new PlasmaOrb();
            OrbCmd.Channel(cs, plasma, player);
            Assert.Equal(1, player.OrbQueue.Capacity, "a character without base orb slots gets one");
            Assert.SequenceEqual(new OrbModel[] { plasma }, player.OrbQueue.Orbs);
            Assert.True(ReferenceEquals(player, plasma.Owner), "the channeled orb belongs to the player");
            var entry = cs.History.OrbChanneledEntries.Single();
            Assert.Equal(PlasmaOrb.OrbIdEntry, entry.OrbId);
            Assert.Equal(player.CombatId, entry.Stamp.ActorId);
            Assert.SequenceEqual(new[] { "channeled:" + PlasmaOrb.OrbIdEntry + ":queued=True" }, log, "AfterOrbChanneled runs after the enqueue");
        });
    }

    public static void Test_CharacterMechanics_ChannelIntoAFullQueueEvokesTheFrontOrbFirst()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_ChannelIntoAFullQueueEvokesTheFrontOrbFirst), Hooks, () =>
        {
            var cs = NewCombat(baseOrbSlots: 3);
            var player = cs.Player;
            player.Energy = 0;
            var log = new List<string>();
            AddProbe(player, log);
            player.OrbQueue.AddCapacity(2);
            var front = Queued<PlasmaOrb>(player);
            var back = Queued<DarkOrb>(player);
            var incoming = new FrostOrb();
            OrbCmd.Channel(cs, incoming, player);
            Assert.Equal(2, player.Energy, "the front Plasma orb was evoked");
            Assert.True(front.HasBeenRemovedFromState, "the evoked orb was removed from the state");
            Assert.SequenceEqual(new OrbModel[] { back, incoming }, player.OrbQueue.Orbs);
            Assert.Equal(2, player.OrbQueue.Capacity, "no slot is added for a character with base slots");
            Assert.SequenceEqual(new[]
            {
                $"evoked:{PlasmaOrb.OrbIdEntry}:targets={player.CombatId}:removed=False",
                $"channeled:{FrostOrb.OrbIdEntry}:queued=True"
            }, log);
            Assert.Equal(1, cs.History.OrbChanneledEntries.Count);
        });
    }

    public static void Test_CharacterMechanics_ChannelWithNoCapacityOnASlottedCharacterEnqueuesNothing()
    {
        var cs = NewCombat(baseOrbSlots: 3);
        var player = cs.Player;
        var plasma = new PlasmaOrb();
        OrbCmd.Channel(cs, plasma, player);
        Assert.Equal(0, player.OrbQueue.Capacity, "only a character without base slots gets the emergency slot");
        Assert.Equal(0, player.OrbQueue.Orbs.Count, "TryEnqueue declines with no capacity");
        Assert.True(plasma.HasOwner, "the owner is set before the enqueue is attempted");
        Assert.Equal(0, cs.History.OrbChanneledEntries.Count, "nothing is recorded");
        Assert.Equal(3, player.Energy, "nothing was evoked");
    }

    public static void Test_CharacterMechanics_ChannelIsANoOpOnceTheCombatIsOverOrEnding()
    {
        var cs = NewCombat();
        MakeEnding(cs);
        var plasma = new PlasmaOrb();
        OrbCmd.Channel(cs, plasma, cs.Player);
        Assert.Equal(0, cs.Player.OrbQueue.Capacity);
        Assert.False(plasma.HasOwner);
        var over = NewCombat();
        MakeOver(over);
        OrbCmd.Channel<PlasmaOrb>(over, over.Player);
        OrbCmd.Channel(over, PlasmaOrb.OrbIdEntry, over.Player);
        Assert.Equal(0, over.Player.OrbQueue.Orbs.Count);
        Assert.Equal(0, over.History.OrbChanneledEntries.Count);
    }

    public static void Test_CharacterMechanics_ChannelByIdAndByTypeCreateFreshOrbs()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_ChannelByIdAndByTypeCreateFreshOrbs), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            OrbCmd.AddSlots(cs, player, 3);
            OrbCmd.Channel<DarkOrb>(cs, player);
            OrbCmd.Channel(cs, GlassOrb.OrbIdEntry, player);
            Assert.SequenceEqual(new[] { DarkOrb.OrbIdEntry, GlassOrb.OrbIdEntry }, player.OrbQueue.Orbs.Select(o => o.Id));
            Assert.SequenceEqual(new[] { DarkOrb.OrbIdEntry, GlassOrb.OrbIdEntry }, cs.History.OrbChanneledEntries.Select(e => e.OrbId));
            Assert.Throws<KeyNotFoundException>(() => OrbCmd.Channel(cs, "NO_SUCH_ORB", player));
        });
    }

    public static void Test_CharacterMechanics_EvokeNextAndEvokeLastTakeTheQueueEnds()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_EvokeNextAndEvokeLastTakeTheQueueEnds), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            player.OrbQueue.AddCapacity(3);
            var a = new RecordingOrb("a", log);
            var b = new RecordingOrb("b", log);
            var c = new RecordingOrb("c", log);
            foreach (var orb in new[] { a, b, c })
            {
                orb.AttachTo(player);
                player.OrbQueue.TryEnqueue(orb);
            }
            OrbCmd.EvokeNext(cs, player);
            OrbCmd.EvokeLast(cs, player);
            Assert.SequenceEqual(new[] { "evoke:a", "evoke:c" }, log);
            Assert.SequenceEqual(new OrbModel[] { b }, player.OrbQueue.Orbs);
            Assert.True(a.HasBeenRemovedFromState);
            Assert.True(c.HasBeenRemovedFromState);
            OrbCmd.EvokeNext(cs, player, dequeue: false);
            OrbCmd.EvokeLast(cs, player, dequeue: false);
            Assert.SequenceEqual(new[] { "evoke:a", "evoke:c", "evoke:b", "evoke:b" }, log);
            Assert.SequenceEqual(new OrbModel[] { b }, player.OrbQueue.Orbs, "an evoke without dequeue keeps the orb");
            Assert.False(b.HasBeenRemovedFromState);
        });
    }

    public static void Test_CharacterMechanics_EvokeDequeuesBeforeEvokeAndRemovesInternalAfterTheHook()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_EvokeDequeuesBeforeEvokeAndRemovesInternalAfterTheHook), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            player.OrbQueue.AddCapacity(2);
            var orb = new RecordingOrb("a", log);
            orb.AttachTo(player);
            player.OrbQueue.TryEnqueue(orb);
            bool? queuedDuringEvoke = null;
            bool? removedDuringEvoke = null;
            orb.OnEvoke = (_, self) =>
            {
                queuedDuringEvoke = player.OrbQueue.Orbs.Contains(self);
                removedDuringEvoke = self.HasBeenRemovedFromState;
            };
            OrbCmd.EvokeNext(cs, player);
            Assert.Equal(false, queuedDuringEvoke, "the orb leaves the queue before its Evoke");
            Assert.Equal(false, removedDuringEvoke, "RemoveInternal has not run during the Evoke");
            Assert.SequenceEqual(new[] { "evoke:a", $"evoked:{orb.Id}:targets={player.CombatId}:removed=False" }, log, "AfterOrbEvoked sees the orb before RemoveInternal");
            Assert.True(orb.HasBeenRemovedFromState, "RemoveInternal runs after AfterOrbEvoked");
        });
    }

    public static void Test_CharacterMechanics_EvokeForAPlayerOutsideTheCombatSkipsTheHookAndRemoveInternal()
    {
        var cs = NewCombat();
        var stranger = new PlayerState(60, "DEFECT", 70, 70, 3) { Energy = 0 };
        stranger.OrbQueue.AddCapacity(2);
        var plasma = Orb<PlasmaOrb>(stranger);
        stranger.OrbQueue.TryEnqueue(plasma);
        OrbCmd.EvokeNext(cs, stranger);
        Assert.Equal(0, stranger.OrbQueue.Orbs.Count, "the orb is dequeued before its Evoke");
        Assert.Equal(2, stranger.Energy, "the Evoke itself runs");
        Assert.False(plasma.HasBeenRemovedFromState, "without a combat for the owner neither AfterOrbEvoked nor RemoveInternal runs");
    }

    public static void Test_CharacterMechanics_EvokeOnAnEmptyQueueOrAfterTheCombatDoesNothing()
    {
        var cs = NewCombat();
        var player = cs.Player;
        OrbCmd.EvokeNext(cs, player);
        OrbCmd.EvokeLast(cs, player);
        Assert.Equal(3, player.Energy);
        player.OrbQueue.AddCapacity(1);
        var plasma = Queued<PlasmaOrb>(player);
        MakeEnding(cs);
        OrbCmd.EvokeNext(cs, player);
        Assert.SequenceEqual(new OrbModel[] { plasma }, player.OrbQueue.Orbs, "Evoke is gated on IsOverOrEnding");
        Assert.Equal(3, player.Energy);
        Assert.False(plasma.HasBeenRemovedFromState);
    }

    public static void Test_CharacterMechanics_GainStarsIsANoOpWhileTheCombatIsEnding()
    {
        var cs = NewCombat();
        var log = new List<string>();
        AddProbe(cs.Player, log);
        MakeEnding(cs);
        StarsCmd.GainStars(cs, cs.Player, 3m);
        StarsCmd.SetStars(cs, cs.Player, 5m);
        Assert.Equal(0, cs.Player.Stars);
        Assert.Equal(0, cs.History.StarsModifiedEntries.Count);
        Assert.Equal(0, log.Count, "ShouldGainStars is not consulted once the combat is ending");
    }

    public static void Test_CharacterMechanics_GainStarsConsultsShouldGainStarsThenRecordsAndNotifies()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_GainStarsConsultsShouldGainStarsThenRecordsAndNotifies), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            var probe = AddProbe(player, log);
            StarsCmd.GainStars(cs, player, 2.7m);
            Assert.Equal(2, player.Stars, "(int)(0 + 2.7)");
            Assert.SequenceEqual(new[] { 2 }, cs.History.StarsModifiedEntries.Select(e => e.Amount));
            Assert.Equal(player.CombatId, cs.History.StarsModifiedEntries[0].Stamp.ActorId);
            Assert.SequenceEqual(new[] { "shouldGainStars:2.7", "starsGained:2" }, log, "AfterStarsGained receives (int)amount");
            log.Clear();
            StarsCmd.GainStars(cs, player, 0.5m);
            Assert.Equal(2, player.Stars, "(int)(2 + 0.5) is unchanged");
            Assert.Equal(1, cs.History.StarsModifiedEntries.Count, "no change, no entry");
            Assert.SequenceEqual(new[] { "shouldGainStars:0.5", "starsGained:0" }, log, "the hook still runs");
            log.Clear();
            probe.AllowStarGain = false;
            StarsCmd.GainStars(cs, player, 4m);
            Assert.Equal(2, player.Stars, "a listener can refuse the gain");
            Assert.SequenceEqual(new[] { "shouldGainStars:4" }, log);
            Assert.Throws<ArgumentException>(() =>
            {
                probe.AllowStarGain = true;
                StarsCmd.GainStars(cs, player, -1m);
            }, "a negative gain is rejected by PlayerCombatState.GainStars");
        });
    }

    public static void Test_CharacterMechanics_LoseStarsClampsAtZeroAndRecordsTheClampedDelta()
    {
        var cs = NewCombat();
        var player = cs.Player;
        player.Stars = 3;
        StarsCmd.LoseStars(cs, player, 1.5m);
        Assert.Equal(1, player.Stars, "(int)(3 - 1.5) = 1: a fractional loss truncates the result");
        StarsCmd.LoseStars(cs, player, 5m);
        Assert.Equal(0, player.Stars, "clamped at 0");
        StarsCmd.LoseStars(cs, player, 2m);
        Assert.SequenceEqual(new[] { -2, -1 }, cs.History.StarsModifiedEntries.Select(e => e.Amount), "the entry is the clamped integer change; no change, no entry");
        Assert.Throws<ArgumentException>(() => StarsCmd.LoseStars(cs, player, -1m));
        player.Stars = 4;
        MakeEnding(cs);
        StarsCmd.LoseStars(cs, player, 2m);
        Assert.Equal(4, player.Stars, "LoseStars is a no-op once the combat is ending");
        var over = NewCombat();
        over.Player.Stars = 4;
        MakeOver(over);
        StarsCmd.LoseStars(over, over.Player, 1m);
        Assert.Equal(3, over.Player.Stars, "the gate is IsEnding, which is false once the combat is over");
    }

    public static void Test_CharacterMechanics_SetStarsLosesDownToTheTarget()
    {
        var cs = NewCombat();
        var player = cs.Player;
        player.Stars = 7;
        StarsCmd.SetStars(cs, player, 4m);
        Assert.Equal(4, player.Stars);
        StarsCmd.SetStars(cs, player, 4m);
        StarsCmd.SetStars(cs, player, -2m);
        Assert.Equal(0, player.Stars, "a negative target loses everything and clamps");
        Assert.SequenceEqual(new[] { -3, -4 }, cs.History.StarsModifiedEntries.Select(e => e.Amount));
    }

    public static void Test_CharacterMechanics_SetStarsGainsUpThroughGainStars()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SetStarsGainsUpThroughGainStars), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            var probe = AddProbe(player, log);
            player.Stars = 1;
            StarsCmd.SetStars(cs, player, 4m);
            Assert.Equal(4, player.Stars);
            Assert.SequenceEqual(new[] { "shouldGainStars:3", "starsGained:3" }, log);
            probe.AllowStarGain = false;
            StarsCmd.SetStars(cs, player, 9m);
            Assert.Equal(4, player.Stars, "the gain half honours ShouldGainStars");
        });
    }

    public static void Test_CharacterMechanics_SpendStarsIgnoresNonPositiveAmounts()
    {
        var cs = NewCombat();
        cs.Player.Stars = 3;
        ulong hash = Hash(cs);
        StarsCmd.SpendStars(cs, cs.Player, 0);
        StarsCmd.SpendStars(cs, cs.Player, -2);
        Assert.Equal(3, cs.Player.Stars);
        Assert.Equal(hash, Hash(cs), "no state change at all");
    }

    public static void Test_CharacterMechanics_SpendStarsHasNoEndingGateAndFiresAfterStarsSpent()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SpendStarsHasNoEndingGateAndFiresAfterStarsSpent), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            player.Stars = 5;
            StarsCmd.SpendStars(cs, player, 2);
            Assert.Equal(3, player.Stars);
            Assert.SequenceEqual(new[] { "starsSpent:2" }, log);
            StarsCmd.SpendStars(cs, player, 5);
            Assert.Equal(0, player.Stars, "an overspend clamps at 0");
            Assert.SequenceEqual(new[] { "starsSpent:2", "starsSpent:5" }, log, "the hook receives the spent amount, not the change");
            Assert.SequenceEqual(new[] { -2, -3 }, cs.History.StarsModifiedEntries.Select(e => e.Amount));
            player.Stars = 4;
            MakeEnding(cs);
            StarsCmd.SpendStars(cs, player, 1);
            Assert.Equal(3, player.Stars, "spending is not gated on IsEnding");
            Assert.Equal(3, cs.History.StarsModifiedEntries.Count);
            Assert.Equal(2, log.Count, "the combat hook listeners are silent once the combat is ending");
        });
    }

    public static void Test_CharacterMechanics_ExcessEnergyCostIsPaidWithTwoStarsPerMissingEnergy()
    {
        var cs = NewCombat();
        var player = cs.Player;
        player.Energy = 1;
        int energyCost = 3;
        int starCost = 1;
        StarsCmd.PayExcessEnergyCostWithStars(cs, player, ref energyCost, ref starCost);
        Assert.Equal(3, energyCost, "without a listener the energy cost stands");
        Assert.Equal(1, starCost);
        AddProbe(player).PayExcessEnergyWithStars = true;
        StarsCmd.PayExcessEnergyCostWithStars(cs, player, ref energyCost, ref starCost);
        Assert.Equal(1, energyCost, "the energy cost drops to the available energy");
        Assert.Equal(5, starCost, "1 + (3 - 1) x 2");
        energyCost = 1;
        starCost = 0;
        StarsCmd.PayExcessEnergyCostWithStars(cs, player, ref energyCost, ref starCost);
        Assert.Equal(1, energyCost, "an affordable cost is untouched");
        Assert.Equal(0, starCost);
        Assert.Equal(2, StarsCmd.StarsPerMissingEnergy);
    }

    public static void Test_CharacterMechanics_StarsResetForNewCombatWritesNoHistory()
    {
        var cs = NewCombat();
        cs.Player.Stars = 6;
        StarsCmd.ResetForNewCombat(cs.Player);
        Assert.Equal(0, cs.Player.Stars);
        Assert.Equal(0, cs.History.Count, "a new PlayerCombatState starts at 0 stars without an entry");
    }

    public static void Test_CharacterMechanics_PetListAddsOncePerPetAndRejectsAnotherOwner()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var pet = new MonsterState(cs.AllocateCreatureId(), new OstyDouble(), 1, 1, null, null) { Side = CombatSide.Player };
        PlayerPets.AddPetInternal(player, pet);
        PlayerPets.AddPetInternal(player, pet);
        Assert.SequenceEqual(new[] { pet.CombatId }, player.PetIds, "a pet is listed once");
        Assert.Equal(player.CombatId, pet.PetOwnerId);
        var other = new PlayerState(70, "NECROBINDER", 60, 60, 3);
        var thrown = Assert.Throws<InvalidOperationException>(() => PlayerPets.AddPetInternal(other, pet));
        Assert.True(thrown.Message.Contains("already has an owner", StringComparison.Ordinal), thrown.Message);
        Assert.Equal(0, other.PetIds.Count);
        var owned = new MonsterState(cs.AllocateCreatureId(), new OstyDouble(), 1, 1, null, null) { PetOwnerId = player.CombatId };
        PlayerPets.AddPetInternal(player, owned);
        Assert.SequenceEqual(new[] { pet.CombatId, owned.CombatId }, player.PetIds, "the owner's own pet is re-added without touching its owner");
        PlayerPets.ResetForNewCombat(player);
        Assert.Equal(0, player.PetIds.Count);
    }

    public static void Test_CharacterMechanics_OnPetDiedDropsThePetOnlyWhenTheHookAllowsRemoval()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var probe = AddProbe(player);
        var pet = new MonsterState(cs.AllocateCreatureId(), new OstyDouble(), 0, 1, null, null) { Side = CombatSide.Player };
        PlayerPets.AddPetInternal(player, pet);
        probe.RemoveCreaturesAfterDeath = false;
        PlayerPets.NotifyPetDied(cs, pet);
        Assert.SequenceEqual(new[] { pet.CombatId }, player.PetIds, "a listener that keeps the creature keeps the pet listed");
        probe.RemoveCreaturesAfterDeath = true;
        PlayerPets.NotifyPetDied(cs, pet);
        Assert.Equal(0, player.PetIds.Count, "otherwise the pet leaves the list");
        PlayerPets.NotifyPetDied(cs, pet);
        PlayerPets.NotifyPetDied(cs, cs.Monsters[0]);
        Assert.Equal(0, player.PetIds.Count, "a creature that is not listed is ignored");
        var thrown = Assert.Throws<InvalidOperationException>(() => PlayerPets.OnPetDied(cs, player, pet));
        Assert.Equal("Player does not have pet " + Osty.MonsterIdEntry, thrown.Message);
    }

    public static void Test_CharacterMechanics_PetLookupsResolveIdsThroughTheAllies()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_PetLookupsResolveIdsThroughTheAllies), State, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            Assert.Null(PlayerPets.GetOsty(cs, player));
            Assert.False(PlayerPets.IsOstyAlive(cs, player));
            Assert.True(PlayerPets.IsOstyMissing(cs, player));
            Assert.True(Osty.CheckMissingWithAnim(cs, player));
            var other = new MonsterState(cs.AllocateCreatureId(), new MonsterDouble(), 5, 5, null, null) { Side = CombatSide.Player };
            cs.AddAlly(other);
            PlayerPets.AddPetInternal(player, other);
            var osty = AddOsty(cs, 3, 3);
            Assert.True(ReferenceEquals(osty, PlayerPets.GetOsty(cs, player)), "GetPet finds the first pet with the id");
            Assert.True(PlayerPets.IsOstyAlive(cs, player));
            Assert.False(Osty.CheckMissingWithAnim(cs, player));
            var pets = new List<MonsterState>();
            PlayerPets.GetPets(cs, player, pets);
            Assert.SequenceEqual(new[] { other, osty }, pets, "pets in insertion order");
            Assert.SequenceEqual(new CreatureState[] { player, other, osty }, cs.Allies, "allies: the player, then pets");
            osty.CurrentHp = 0;
            Assert.True(PlayerPets.IsOstyMissing(cs, player), "a dead Osty is missing");
            Assert.True(ReferenceEquals(osty, PlayerPets.GetOsty(cs, player)), "but still listed");
        });
    }

    public static void Test_CharacterMechanics_SummonWithAZeroAmountReturnsTheCurrentOsty()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SummonWithAZeroAmountReturnsTheCurrentOsty), Hooks, () =>
        {
            var cs = NewCombat();
            var log = new List<string>();
            AddProbe(cs.Player, log).SummonAmountOverride = 0m;
            var result = OstyCmd.Summon(cs, cs.Player, 5m, null);
            Assert.Null(result.Creature);
            Assert.Equal(0m, result.Amount);
            Assert.Equal(0, cs.History.Count, "nothing is recorded");
            Assert.Equal(0, log.Count, "AfterSummon does not run");
            Assert.Equal(1, cs.Allies.Count, "no Osty was created");
        });
    }

    public static void Test_CharacterMechanics_SummonRaisesMaxHpOfALivingOsty()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SummonRaisesMaxHpOfALivingOsty), HooksAndState, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            var osty = AddOsty(cs, 4, 5);
            var result = OstyCmd.Summon(cs, player, 3m, null);
            Assert.Equal(8, osty.MaxHp, "GainMaxHp(amount)");
            Assert.Equal(7, osty.CurrentHp, "the max HP gain is healed");
            Assert.True(ReferenceEquals(osty, result.Creature));
            Assert.Equal(3m, result.Amount);
            Assert.SequenceEqual(new[] { 3 }, cs.History.SummonedEntries.Select(e => e.Amount));
            Assert.Equal(player.CombatId, cs.History.SummonedEntries[0].Stamp.ActorId);
            Assert.SequenceEqual(new[] { "summon:3" }, log, "no revive hook for a living Osty");
            Assert.SequenceEqual(new[] { osty.CombatId }, player.PetIds);
        });
    }

    public static void Test_CharacterMechanics_SummonRevivesADeadOsty()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SummonRevivesADeadOsty), HooksAndState, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            var osty = AddOsty(cs, 0, 9);
            var result = OstyCmd.Summon(cs, player, 4.6m, null);
            Assert.Equal(4, osty.MaxHp, "SetMaxHp(amount) replaces the old maximum");
            Assert.Equal(4, osty.CurrentHp, "Heal(amount)");
            Assert.SequenceEqual(new[] { $"revived:{osty.CombatId}", "summon:4.6" }, log, "AfterOstyRevived, then AfterSummon with the decimal amount");
            Assert.SequenceEqual(new[] { 4 }, cs.History.SummonedEntries.Select(e => e.Amount), "the entry stores (int)amount");
            Assert.SequenceEqual(new[] { osty.CombatId }, player.PetIds, "the listed Osty is not listed twice");
            Assert.True(ReferenceEquals(osty, result.Creature));
            Assert.Equal(4.6m, result.Amount);
            Assert.Equal(2, cs.Allies.Count, "no second Osty");
        });
    }

    public static void Test_CharacterMechanics_SummonRevivesAnOstyThatLeftThePetList()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SummonRevivesAnOstyThatLeftThePetList), HooksAndState, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            var osty = AddOsty(cs, 0, 6, inPetList: false);
            Assert.Null(PlayerPets.GetOsty(cs, player));
            OstyCmd.Summon(cs, player, 5m, null);
            Assert.SequenceEqual(new[] { osty.CombatId }, player.PetIds, "the dead Osty found among the allies is listed again");
            Assert.Equal(5, osty.CurrentHp);
            Assert.SequenceEqual(new[] { $"revived:{osty.CombatId}", "summon:5" }, log);
        });
    }

    public static void Test_CharacterMechanics_SummonRejectsALivingOstyMissingFromThePetList()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SummonRejectsALivingOstyMissingFromThePetList), HooksAndState, () =>
        {
            var cs = NewCombat();
            AddOsty(cs, 3, 3, inPetList: false);
            var thrown = Assert.Throws<InvalidOperationException>(() => OstyCmd.Summon(cs, cs.Player, 2m, null));
            Assert.Equal("We shouldn't make it here if Osty is still alive!", thrown.Message);
        });
    }

    public static void Test_CharacterMechanics_SummonCreatesOstyThroughAddPetAndDieForYou()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_SummonCreatesOstyThroughAddPetAndDieForYou), HooksLoopPipelineAndState, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            SummonResult result;
            try
            {
                result = OstyCmd.Summon(cs, player, 6m, null);
            }
            catch (KeyNotFoundException ex)
            {
                Assert.False(PowerRegistry.AllIds.Contains(OstyCmd.DieForYouPowerId), "only an unregistered pilot may be missing");
                Assert.True(ex.Message.Contains("'" + OstyCmd.DieForYouPowerId + "'", StringComparison.Ordinal), "DieForYouPower is looked up by its exact id: " + ex.Message);
                return;
            }
            var osty = Assert.NotNull(result.Creature);
            Assert.Equal(Osty.MonsterIdEntry, osty.MonsterId);
            Assert.Equal(CombatSide.Player, osty.Side);
            Assert.Equal(player.CombatId, osty.PetOwnerId);
            Assert.SequenceEqual(new[] { osty.CombatId }, player.PetIds);
            Assert.True(cs.Allies.Contains(osty), "the pet joins the allies");
            Assert.False(cs.Monsters.Contains(osty), "a pet is never an enemy");
            Assert.Equal(6, osty.MaxHp, "SetMaxHp(amount) after the 1-HP creation");
            Assert.Equal(6, osty.CurrentHp);
            var dieForYou = Assert.NotNull(osty.GetPower(OstyCmd.DieForYouPowerId));
            Assert.Equal(1, dieForYou.Amount);
            Assert.SequenceEqual(new[] { "summon:6" }, log, "a created Osty is not revived");
            Assert.SequenceEqual(new[] { 6 }, cs.History.SummonedEntries.Select(e => e.Amount));
            Assert.True(ReferenceEquals(osty, PlayerPets.GetOsty(cs, player)));
        });
    }

    public static void Test_CharacterMechanics_ForgeIsANoOpOnceTheCombatIsOverOrEnding()
    {
        var cs = NewCombat();
        var blade = AddCard(cs, new BladeDouble(), cs.Player.Hand);
        MakeEnding(cs);
        Assert.Equal(0, ForgeCmd.Forge(cs, 5m, cs.Player, null).Count);
        Assert.Equal(10m, blade.Vars.Damage.BaseValue);
        var over = NewCombat();
        MakeOver(over);
        Assert.Equal(0, ForgeCmd.Forge(over, 5m, over.Player, null).Count);
        Assert.Equal(0, over.Player.Hand.Count, "no blade is created either");
    }

    public static void Test_CharacterMechanics_GetSovereignBladesFiltersDupesAndOptionallyTheExhaustPile()
    {
        var cs = NewCombat();
        var player = cs.Player;
        var def = new BladeDouble();
        var draw = AddCard(cs, def, player.DrawPile);
        var hand = AddCard(cs, def, player.Hand);
        var exhausted = AddCard(cs, def, player.ExhaustPile);
        AddCard(cs, def, player.DiscardPile, dupe: true);
        var played = AddCard(cs, def, player.PlayPile);
        AddCard(cs, new OtherCardDouble(), player.Hand);
        Assert.SequenceEqual(new[] { hand, draw, played }, ForgeCmd.GetSovereignBlades(player, includeExhausted: false), "pile order Hand, Draw, Discard, Exhaust, Play; dupes and the exhaust pile skipped");
        Assert.SequenceEqual(new[] { hand, draw, exhausted, played }, ForgeCmd.GetSovereignBlades(player, includeExhausted: true));
    }

    public static void Test_CharacterMechanics_ForgeCountsNonDupeBladesAndForgesExhaustedOnes()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_ForgeCountsNonDupeBladesAndForgesExhaustedOnes), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            var probe = AddProbe(player, log);
            var def = new BladeDouble();
            var hand = AddCard(cs, def, player.Hand);
            var exhausted = AddCard(cs, def, player.ExhaustPile);
            var dupe = AddCard(cs, def, player.DiscardPile, dupe: true);
            int cardIds = cs.NextCardInstanceId;
            var returned = ForgeCmd.Forge(cs, 4m, player, probe);
            Assert.SequenceEqual(new[] { hand }, returned, "the result lists the counted blades");
            Assert.Equal(14m, hand.Vars.Damage.BaseValue);
            Assert.Equal(14m, exhausted.Vars.Damage.BaseValue, "an exhausted blade is still forged");
            Assert.Equal(10m, dupe.Vars.Damage.BaseValue, "a dupe is never forged");
            Assert.Equal(cardIds, cs.NextCardInstanceId, "a blade outside the exhaust pile exists, so none is created");
            Assert.SequenceEqual(new[] { "forge:4:source=set" }, log, "AfterForge carries the amount and the source");
            Assert.Equal(0, cs.Run.Fidelity.Count, "a def with the seam needs no flag");
        });
    }

    public static void Test_CharacterMechanics_ForgeCreatesABladeWhenOnlyExhaustedOrDupeBladesExist()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_ForgeCreatesABladeWhenOnlyExhaustedOrDupeBladesExist), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            var log = new List<string>();
            AddProbe(player, log);
            var def = new BladeDouble();
            var exhausted = AddCard(cs, def, player.ExhaustPile);
            var dupe = AddCard(cs, def, player.Hand, dupe: true);
            IReadOnlyList<CardInstance> returned;
            try
            {
                returned = ForgeCmd.Forge(cs, 3m, player, null);
            }
            catch (KeyNotFoundException ex)
            {
                Assert.False(CardRegistry.Defs.Contains(ForgeCmd.SovereignBladeId), "only an unregistered pilot may be missing");
                Assert.True(ex.Message.Contains("'" + ForgeCmd.SovereignBladeId + "'", StringComparison.Ordinal), "the blade is looked up by its exact id: " + ex.Message);
                Assert.Equal(10m, exhausted.Vars.Damage.BaseValue, "the lookup comes before any forging");
                return;
            }
            var created = returned.Single();
            Assert.Equal(ForgeCmd.SovereignBladeId, created.Id);
            Assert.True(ReferenceEquals(player, created.Owner));
            Assert.True(player.Hand.Contains(created), "the new blade is generated into the hand");
            decimal canonicalDamage = CardRegistry.Get(ForgeCmd.SovereignBladeId).CreateVars().Damage.BaseValue;
            Assert.Equal(canonicalDamage + 3m, created.Vars.Damage.BaseValue, "the new blade is forged too");
            Assert.Equal(13m, exhausted.Vars.Damage.BaseValue, "the exhausted blade is forged as well");
            Assert.Equal(10m, dupe.Vars.Damage.BaseValue);
            Assert.SequenceEqual(new[] { "forge:3:source=none" }, log);
            Assert.False(cs.Run.Fidelity.Entries.Any(e => e.Site == ForgeCmd.AddDamageFallbackSite), "the registered SOVEREIGN_BLADE takes the ISovereignBladeDef seam");
        });
    }

    public static void Test_CharacterMechanics_ForgeFallbackForADefWithoutTheSeamRecordsAFidelityFlag()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_ForgeFallbackForADefWithoutTheSeamRecordsAFidelityFlag), Hooks, () =>
        {
            var cs = NewCombat();
            var blade = AddCard(cs, new SeamlessBladeDouble(), cs.Player.Hand);
            ForgeCmd.Forge(cs, 2m, cs.Player, null);
            Assert.Equal(12m, blade.Vars.Damage.BaseValue, "the fallback adds to the Damage var");
            var entry = cs.Run.Fidelity.Entries.Single();
            Assert.Equal(FidelityFlag.ApproximatedRule, entry.Flag);
            Assert.Equal(ForgeCmd.AddDamageFallbackSite, entry.Site);
        });
    }

    public static void Test_CharacterMechanics_OrbQueueCloneIsIsolatedAndHashesEqual()
    {
        var cs = NewCombat(baseOrbSlots: 3);
        var player = cs.Player;
        player.Stars = 2;
        player.OrbQueue.AddCapacity(3);
        Queued<PlasmaOrb>(player);
        Queued<DarkOrb>(player);
        var clone = cs.DeepClone();
        Assert.Equal(Hash(cs), Hash(clone));
        var cloneOrbs = clone.Player.OrbQueue.Orbs;
        Assert.Equal(2, cloneOrbs.Count);
        for (int i = 0; i < cloneOrbs.Count; i++)
        {
            Assert.False(ReferenceEquals(cloneOrbs[i], player.OrbQueue.Orbs[i]), "orbs are cloned");
            Assert.True(ReferenceEquals(clone.Player, cloneOrbs[i].Owner), "the cloned orb belongs to the cloned player");
        }
        AssertCloneHashesEqualAndIsolated(cs, c => c.Player.OrbQueue.AddCapacity(1), "capacity");
        AssertCloneHashesEqualAndIsolated(cs, c => c.Player.OrbQueue.Remove(c.Player.OrbQueue.Orbs[0]), "orb list");
        AssertCloneHashesEqualAndIsolated(cs, c => c.Player.OrbQueue.Orbs[1].RemoveInternal(), "removed flag");
        AssertCloneHashesEqualAndIsolated(cs, c => c.Player.PetIds.Add(42), "pet list");
        AssertCloneHashesEqualAndIsolated(cs, c => StarsCmd.LoseStars(c, c.Player, 1m), "stars and their history entry");
        AssertCloneHashesEqualAndIsolated(cs, c => c.Player.OrbQueue.Orbs[0].Evoke(c), "an orb's evoke");
    }

    public static void Test_CharacterMechanics_DarkAndGlassAccumulatedValuesChangeTheHash()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_DarkAndGlassAccumulatedValuesChangeTheHash), Hooks, () =>
        {
            var cs = NewCombat();
            var player = cs.Player;
            player.OrbQueue.AddCapacity(2);
            Queued<DarkOrb>(player);
            Queued<GlassOrb>(player);
            AssertCloneHashesEqualAndIsolated(cs, c => c.Player.OrbQueue.Orbs[0].Passive(c, null), "Dark evoke value");
            AssertCloneHashesEqualAndIsolated(cs, c => c.Player.OrbQueue.Orbs[1].Passive(c, null), "Glass passive value");
        });
    }

    public static void Test_CharacterMechanics_CommandsKeepCloneAndHashEquality()
    {
        W1Pending.Run(nameof(Test_CharacterMechanics_CommandsKeepCloneAndHashEquality), HooksAndState, () =>
        {
            var cs = NewCombat(monsters: 2);
            var player = cs.Player;
            OrbCmd.AddSlots(cs, player, 3);
            OrbCmd.Channel<LightningOrb>(cs, player);
            OrbCmd.Channel<DarkOrb>(cs, player);
            var blade = AddCard(cs, new BladeDouble(), player.Hand);
            AddOsty(cs, 2, 4);
            player.Stars = 5;
            var operations = new (string Label, Action<CombatState> Op)[]
            {
                ("channel", c => OrbCmd.Channel<FrostOrb>(c, c.Player)),
                ("evoke", c => OrbCmd.EvokeNext(c, c.Player)),
                ("summon", c => OstyCmd.Summon(c, c.Player, 2m, null)),
                ("forge", c => ForgeCmd.Forge(c, 3m, c.Player, null)),
                ("gain stars", c => StarsCmd.GainStars(c, c.Player, 2m)),
                ("spend stars", c => StarsCmd.SpendStars(c, c.Player, 2)),
                ("remove slot", c => OrbCmd.RemoveSlots(c, c.Player, 1))
            };
            foreach (var (label, op) in operations)
            {
                AssertCloneHashesEqualAndIsolated(cs, op, label);
                op(cs);
                var again = cs.DeepClone();
                Assert.Equal(Hash(cs), Hash(again), label + ": equal after the operation");
            }
            Assert.Equal(13m, blade.Vars.Damage.BaseValue, "the source was forged once by the loop above");
        });
    }
}
