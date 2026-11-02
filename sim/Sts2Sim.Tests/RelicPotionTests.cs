using System.Reflection;
using System.Runtime.ExceptionServices;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Potions;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Tests;

public static class RelicPotionTests
{
    private const string Hooks = "hooks";
    private const string Choice = "choice";
    private const string CardsKernel = "cards-kernel";
    private const string Loop = "loop";
    private const string Pipeline = "pipeline";

    private static readonly MethodInfo OnUseMethod = typeof(PotionModel).GetMethod("OnUse", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("RELIC_POTION_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class CardDouble : CardDef
    {
        public CardDouble(string id, CardType type, int maxUpgradeLevel = 1)
            : base(id, 1, type, CardRarity.Common, type == CardType.Attack ? TargetType.AnyEnemy : TargetType.Self)
        {
            MaxUpgrades = maxUpgradeLevel;
        }

        private int MaxUpgrades { get; }

        public override int MaxUpgradeLevel => MaxUpgrades;

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class TestRelic : RelicModel
    {
        private readonly string _id;
        private readonly RelicRarity _rarity;

        public TestRelic(string id = "RELIC_POTION_TEST_RELIC", RelicRarity rarity = RelicRarity.Common, List<string>? log = null)
        {
            _id = id;
            _rarity = rarity;
            Log = log ?? new List<string>();
        }

        public List<string> Log { get; private set; }

        public bool UsedUp { get; set; }

        public bool PickupEffect { get; set; }

        public bool Pets { get; set; }

        public bool Stackable { get; set; }

        public override string Id => _id;

        public override RelicRarity Rarity => _rarity;

        public override bool IsUsedUp => UsedUp;

        public override bool HasUponPickupEffect => PickupEffect;

        public override bool SpawnsPets => Pets;

        public override bool IsStackable => Stackable;

        public override void AfterObtained(RunState rs) => Log.Add(_id + ":obtained");

        public override void AfterRemoved(RunState rs) => Log.Add(_id + ":removed");

        public static bool BeforeAct3(int playerCount, int totalFloor) => IsBeforeAct3TreasureChest(playerCount, totalFloor);

        public static bool BeforeAct3(RunState rs) => IsBeforeAct3TreasureChest(rs);

        protected override void CopyMutableReferenceState(RelicModel source)
        {
            Log = new List<string>(((TestRelic)source).Log);
        }
    }

    private sealed class TestPotion : PotionModel
    {
        private readonly PotionUsage _usage;
        private readonly TargetType _targetType;

        public TestPotion(PotionUsage usage = PotionUsage.CombatOnly, TargetType targetType = TargetType.Self, List<string>? log = null)
        {
            _usage = usage;
            _targetType = targetType;
            Log = log ?? new List<string>();
        }

        public List<string> Log { get; }

        public bool CustomUsable { get; set; } = true;

        public Action<RunState, CreatureState?>? Effect { get; set; }

        public int DepthDuringOnUse { get; private set; } = -1;

        public override string Id => "RELIC_POTION_TEST_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => _usage;

        public override TargetType TargetType => _targetType;

        public override bool PassesCustomUsabilityCheck => CustomUsable;

        protected override void OnUse(RunState rs, CreatureState? target)
        {
            DepthDuringOnUse = rs.Combat?.CardOrPotionEffectDepth ?? -1;
            Log.Add("on-use:" + (target?.CombatId.ToString() ?? "none"));
            Effect?.Invoke(rs, target);
        }
    }

    private sealed class RecordingPower : PowerModel
    {
        private readonly List<string> _log;

        public RecordingPower(List<string> log)
        {
            _log = log;
        }

        public bool AllowProcure { get; set; } = true;

        public override string Id => "RELIC_POTION_RECORDING_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override void BeforePotionUsed(RunState rs, PotionModel potion, CreatureState? target)
            => _log.Add("before-used:" + potion.Id + ":removed=" + potion.HasBeenRemovedFromState + ":depth=" + (rs.Combat?.CardOrPotionEffectDepth ?? -1));

        public override void AfterPotionUsed(RunState rs, PotionModel potion, CreatureState? target)
            => _log.Add("after-used:" + potion.Id + ":depth=" + (rs.Combat?.CardOrPotionEffectDepth ?? -1) + ":history=" + (rs.Combat?.History.PotionUsedEntries.Count ?? -1));

        public override void AfterHandEmptied(CombatState cs, PlayerState player) => _log.Add("hand-emptied");

        public override bool ShouldProcurePotion(RunState rs, PotionModel potion, PlayerState player)
        {
            _log.Add("should-procure:" + potion.Id);
            return AllowProcure;
        }

        public override void AfterPotionProcured(RunState rs, PotionModel potion) => _log.Add("procured:" + potion.Id);

        public override void AfterPotionDiscarded(RunState rs, PotionModel potion) => _log.Add("discarded:" + potion.Id + ":removed=" + potion.HasBeenRemovedFromState);
    }

    private static CombatState NewCombat(int monsters = 1, int monsterHp = 50, int playerHp = 80, ulong seed = 12345UL, UnlockProfile? unlocks = null, string characterId = StartingDecks.IroncladCharacterId)
    {
        var player = new PlayerState(0, characterId, playerHp, playerHp, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(seed), 0, unlocks);
        var cs = new CombatState(run, player, "RELIC_POTION_TEST");
        cs.IsInProgress = true;
        var def = new MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(i + 1, def, monsterHp, monsterHp, null, null));
        }
        cs.NextCreatureId = monsters + 1;
        return cs;
    }

    private static CardInstance AddCard(CombatState cs, CardPile pile, CardDef def)
    {
        var card = new CardInstance(def, cs.AllocateCardInstanceId());
        card.AttachTo(cs.Player);
        pile.AddInternal(card);
        return card;
    }

    private static T Equip<T>(CombatState cs, T relic) where T : RelicModel
    {
        relic.AttachTo(cs.Player);
        cs.Player.Relics.Add(relic);
        return relic;
    }

    private static T Slot<T>(CombatState cs, T potion, int slot) where T : PotionModel
    {
        potion.AttachTo(cs.Player);
        cs.Player.PotionSlots[slot] = potion;
        return potion;
    }

    private static void InvokeOnUse(PotionModel potion, RunState rs, CreatureState? target)
    {
        try
        {
            OnUseMethod.Invoke(potion, new object?[] { rs, target });
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }
    }

    private static void RequireListeners(CombatState cs, params AbstractModel[] models)
    {
        var buffer = new List<AbstractModel>();
        Hook.IterateHookListeners(cs, buffer);
        foreach (var model in models)
        {
            if (!buffer.Contains(model))
            {
                throw new NotImplementedException($"hooks: the combat listener list does not include {model} yet (relics after the player's powers, then potion slots, Combat/CombatState.cs:874-889; CONTRACTS Part B section B3)");
            }
        }
    }

    private static ulong Hash(CombatState cs) => StateHasher.Hash(cs);

    private sealed class RecordingAnswerer : IChoiceAnswerer
    {
        private readonly int[] _picks;

        public RecordingAnswerer(params int[] picks)
        {
            _picks = picks;
        }

        public List<ChoicePrompt> Prompts { get; } = new();

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            Prompts.Add(prompt);
            return new ChoiceAnswer(_picks, false);
        }
    }

    public static void Test_RelicPotion_RelicBaseDefaultsMatchTheGame()
    {
        var relic = new TestRelic();
        Assert.True(relic.IsAllowedInShops);
        Assert.False(relic.IsUsedUp);
        Assert.False(relic.HasUponPickupEffect);
        Assert.False(relic.SpawnsPets);
        Assert.False(relic.IsStackable);
        Assert.False(relic.AddsPet);
        Assert.False(relic.ShowCounter);
        Assert.Equal(0, relic.DisplayAmount);
        Assert.Equal(1, relic.StackCount);
        Assert.Equal(RelicStatus.Normal, relic.Status);
        Assert.False(relic.IsWax);
        Assert.False(relic.IsMelted);
        Assert.False(relic.HasBeenRemovedFromState);
        Assert.Equal(0, relic.FloorAddedToDeck);
        Assert.Equal(0, relic.DynamicVars.Count);
        Assert.False(relic.HasOwner);
        Assert.Throws<InvalidOperationException>(() => _ = relic.Owner);
        var cs = NewCombat();
        Assert.True(relic.IsAllowed(cs.Run));
        Assert.True(relic.IsAllowedAtNeow(cs.Run, cs.Player));
        relic.AfterObtained(cs.Run);
        relic.AfterRemoved(cs.Run);
        Assert.SequenceEqual(new[] { "RELIC_POTION_TEST_RELIC:obtained", "RELIC_POTION_TEST_RELIC:removed" }, relic.Log);
    }

    public static void Test_RelicPotion_MerchantCostIsTheRaritySwitch()
    {
        var expected = new (RelicRarity Rarity, int Cost)[]
        {
            (RelicRarity.Common, 175), (RelicRarity.Uncommon, 225), (RelicRarity.Rare, 275), (RelicRarity.Shop, 200),
            (RelicRarity.Ancient, 999999999), (RelicRarity.Starter, 999999999), (RelicRarity.Event, 999999999), (RelicRarity.None, 1)
        };
        foreach (var (rarity, cost) in expected)
        {
            Assert.Equal(cost, new TestRelic(rarity: rarity).MerchantCost, rarity.ToString());
        }
        Assert.Throws<InvalidOperationException>(() => _ = new TestRelic(rarity: (RelicRarity)8).MerchantCost);
    }

    public static void Test_RelicPotion_IsTradableChecksInTheGameOrder()
    {
        foreach (var rarity in new[] { RelicRarity.None, RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare, RelicRarity.Shop })
        {
            Assert.True(new TestRelic(rarity: rarity).IsTradable, rarity.ToString());
        }
        foreach (var rarity in new[] { RelicRarity.Starter, RelicRarity.Event, RelicRarity.Ancient })
        {
            Assert.False(new TestRelic(rarity: rarity).IsTradable, rarity.ToString());
        }
        Assert.False(new TestRelic { UsedUp = true }.IsTradable);
        Assert.False(new TestRelic { PickupEffect = true }.IsTradable);
        Assert.False(new TestRelic { IsMelted = true }.IsTradable);
        Assert.False(new TestRelic { Pets = true }.IsTradable);
        Assert.False(new Whetstone().IsTradable, "Whetstone has an upon-pickup effect");
        Assert.True(new HappyFlower().IsTradable);
    }

    public static void Test_RelicPotion_StackCountAndRemovalFlagsChangeOnlyThroughThePortedMutators()
    {
        var relic = new TestRelic();
        var ex = Assert.Throws<InvalidOperationException>(() => relic.IncrementStackCount());
        Assert.True(ex.Message.Contains("not a stackable relic", StringComparison.Ordinal));
        Assert.Equal(1, relic.StackCount);
        var stackable = new TestRelic { Stackable = true };
        stackable.IncrementStackCount();
        stackable.IncrementStackCount();
        Assert.Equal(3, stackable.StackCount);
        Assert.True(typeof(RelicModel).GetProperty(nameof(RelicModel.StackCount))!.SetMethod!.IsPrivate);
        Assert.True(typeof(RelicModel).GetProperty(nameof(RelicModel.HasBeenRemovedFromState))!.SetMethod!.IsPrivate, "private as in the game (Models/RelicModel.cs:420-427) now that PlayerState.RemoveRelicInternal calls RemoveInternal (Entities.Players/Player.cs:913)");
        Assert.True(typeof(PotionModel).GetProperty(nameof(PotionModel.HasBeenRemovedFromState))!.SetMethod!.IsPrivate);
        relic.RemoveInternal();
        Assert.True(relic.HasBeenRemovedFromState);
    }

    public static void Test_RelicPotion_IsBeforeAct3TreasureChestThresholds()
    {
        Assert.True(TestRelic.BeforeAct3(1, 40));
        Assert.False(TestRelic.BeforeAct3(1, 41));
        Assert.True(TestRelic.BeforeAct3(2, 37));
        Assert.False(TestRelic.BeforeAct3(2, 38));
        Assert.True(TestRelic.BeforeAct3(NewCombat().Run), "standalone run: TotalFloor 0");
    }

    public static void Test_RelicPotion_RelicPoolsResolveTheFirstPoolInModelDbOrder()
    {
        Assert.SequenceEqual(
            new[]
            {
                RelicPools.IroncladRelicPool, RelicPools.SilentRelicPool, RelicPools.RegentRelicPool, RelicPools.NecrobinderRelicPool,
                RelicPools.DefectRelicPool, RelicPools.DeprecatedRelicPool, RelicPools.EventRelicPool, RelicPools.FallbackRelicPool, RelicPools.SharedRelicPool
            },
            RelicPools.AllRelicPools.Select(p => p.Id));
        Assert.SequenceEqual(new[] { 8, 8, 8, 8, 8, 1, 142, 1, 118 }, RelicPools.AllRelicPools.Select(p => p.AllRelicIds.Count));
        Assert.SequenceEqual(
            new[] { "BRIMSTONE", "BURNING_BLOOD", "CHARONS_ASHES", "DEMON_TONGUE", "PAPER_PHROG", "RED_SKULL", "RUINED_HELMET", "SELF_FORMING_CLAY" },
            RelicPools.Get(RelicPools.IroncladRelicPool).AllRelicIds);
        Assert.Equal(RelicPools.IroncladRelicPool, new PaperPhrog().Pool);
        Assert.Equal(RelicPools.SilentRelicPool, new PaperKrane().Pool);
        foreach (var relic in new RelicModel[] { new GamblingChip(), new HappyFlower(), new CentennialPuzzle(), new Whetstone(), new Nunchaku() })
        {
            Assert.Equal(RelicPools.SharedRelicPool, relic.Pool, relic.Id);
        }
        Assert.Equal(RelicPools.EventRelicPool, RelicPools.PoolOf("LASTING_CANDY"), "LASTING_CANDY is in the Event and Shared pools; the first pool wins");
        Assert.Equal(RelicPools.EventRelicPool, RelicPools.PoolOf("RAZOR_TOOTH"));
        Assert.Equal(RelicPools.FallbackRelicPool, RelicPools.PoolOf("CIRCLET"));
        Assert.Throws<InvalidOperationException>(() => _ = new TestRelic().Pool);
        Assert.Throws<KeyNotFoundException>(() => RelicPools.Get("NO_SUCH_POOL"));
        var shared = RelicPools.Get(RelicPools.SharedRelicPool);
        Assert.Equal(shared.AllRelicIds.Count, shared.AllRelicIds.Distinct().Count(), "pool members are distinct");
    }

    public static void Test_RelicPotion_PotionPoolsMatchTheDecompileAndTheRegisteredPilots()
    {
        Assert.SequenceEqual(
            new[]
            {
                PotionPools.IroncladPotionPool, PotionPools.SilentPotionPool, PotionPools.RegentPotionPool, PotionPools.NecrobinderPotionPool,
                PotionPools.DefectPotionPool, PotionPools.DeprecatedPotionPool, PotionPools.EventPotionPool, PotionPools.MockPotionPool,
                PotionPools.SharedPotionPool, PotionPools.TokenPotionPool
            },
            PotionPools.AllPotionPools.Select(p => p.Id));
        Assert.SequenceEqual(new[] { 3, 3, 3, 3, 3, 1, 3, 1, 45, 1 }, PotionPools.AllPotionPools.Select(p => p.AllPotions.Count));
        Assert.SequenceEqual(new[] { "BLOOD_POTION", "SOLDIERS_STEW", "ASHWATER" }, PotionPools.Get(PotionPools.IroncladPotionPool).AllPotions.Select(p => p.Id));
        var shared = PotionPools.Get(PotionPools.SharedPotionPool).AllPotions;
        Assert.Equal(15, shared.Count(p => p.Rarity == PotionRarity.Common));
        Assert.Equal(15, shared.Count(p => p.Rarity == PotionRarity.Uncommon));
        Assert.Equal(15, shared.Count(p => p.Rarity == PotionRarity.Rare));
        Assert.SequenceEqual(
            new[] { "FAIRY_IN_A_BOTTLE", "FRUIT_JUICE", "REGEN_POTION" },
            PotionPools.AllCanonicalPotions.Where(p => !p.CanBeGeneratedInCombat).Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.Equal(66, PotionPools.AllCanonicalPotions.Count);
        foreach (var id in PotionRegistry.AllIds)
        {
            var potion = PotionRegistry.Create(id);
            var canonical = PotionPools.Canonical(id);
            Assert.Equal(canonical.Rarity, potion.Rarity, id);
            Assert.Equal(canonical.CanBeGeneratedInCombat, potion.CanBeGeneratedInCombat, id);
            Assert.True(ReferenceEquals(canonical, potion.CanonicalInstance), id);
            Assert.True(PotionPools.Get(potion.Pool).Contains(id), id);
            Assert.False(canonical.ToMutable().HasOwner, id);
            Assert.Equal(potion.GetType(), canonical.ToMutable().GetType(), id);
        }
        Assert.Equal(PotionPools.IroncladPotionPool, new Ashwater().Pool);
        Assert.Equal(PotionPools.SharedPotionPool, new FairyInABottle().Pool);
        Assert.Equal(PotionPools.IroncladPotionPool, PotionPools.ForCharacter("IRONCLAD").Id);
        Assert.Equal(PotionPools.SilentPotionPool, PotionPools.ForCharacter("SILENT").Id);
        Assert.Equal(PotionPools.DefectPotionPool, PotionPools.ForCharacter("DEFECT").Id);
        Assert.Equal(PotionPools.IroncladPotionPool, PotionPools.ForCharacter("DEPRIVED").Id);
        Assert.Throws<KeyNotFoundException>(() => PotionPools.ForCharacter("NOBODY"));
        Assert.Throws<KeyNotFoundException>(() => PotionPools.Canonical("NO_SUCH_POTION"));
        Assert.Throws<InvalidOperationException>(() => _ = new TestPotion().Pool);
    }

    public static void Test_RelicPotion_UnlockedPotionsFollowTheEpochGates()
    {
        var shared = PotionPools.Get(PotionPools.SharedPotionPool);
        var ironclad = PotionPools.Get(PotionPools.IroncladPotionPool);
        Assert.Equal(45, shared.GetUnlockedPotions(UnlockProfile.All).Count);
        Assert.Equal(3, ironclad.GetUnlockedPotions(UnlockProfile.All).Count);
        Assert.Equal(0, ironclad.GetUnlockedPotions(UnlockProfile.None).Count);
        var locked = new[] { "BEETLE_JUICE", "MAZALETHS_GIFT", "DROPLET_OF_PRECOGNITION", "POWDERED_DEMISE", "SHIP_IN_A_BOTTLE", "TOUCH_OF_INSANITY" };
        Assert.SequenceEqual(
            shared.AllPotions.Select(p => p.Id).Where(id => !locked.Contains(id)),
            shared.GetUnlockedPotions(UnlockProfile.None).Select(p => p.Id));
        var potion1Only = new UnlockProfile(new[] { PotionPools.Potion1Epoch }, Array.Empty<string>(), 0);
        Assert.SequenceEqual(
            shared.AllPotions.Select(p => p.Id).Where(id => id is not ("POWDERED_DEMISE" or "SHIP_IN_A_BOTTLE" or "TOUCH_OF_INSANITY")),
            shared.GetUnlockedPotions(potion1Only).Select(p => p.Id));
        var ironcladOnly = new UnlockProfile(new[] { PotionPools.Ironclad4Epoch }, Array.Empty<string>(), 0);
        Assert.Equal(3, ironclad.GetUnlockedPotions(ironcladOnly).Count);
        Assert.Equal(0, PotionPools.Get(PotionPools.SilentPotionPool).GetUnlockedPotions(ironcladOnly).Count);
        Assert.Equal(3, PotionPools.Get(PotionPools.EventPotionPool).GetUnlockedPotions(UnlockProfile.None).Count);
    }

    public static void Test_RelicPotion_PotionOptionsAreTheCharacterPoolThenShared()
    {
        var cs = NewCombat();
        var options = PotionFactory.GetPotionOptions(cs.Run, cs.Player).Select(p => p.Id).ToList();
        Assert.Equal(48, options.Count);
        Assert.SequenceEqual(new[] { "BLOOD_POTION", "SOLDIERS_STEW", "ASHWATER" }, options.Take(3));
        Assert.SequenceEqual(PotionPools.Get(PotionPools.SharedPotionPool).AllPotions.Select(p => p.Id), options.Skip(3));
        var silent = NewCombat(characterId: "SILENT");
        Assert.SequenceEqual(new[] { "POISON_POTION", "GHOST_IN_A_JAR", "CUNNING_POTION" }, PotionFactory.GetPotionOptions(silent.Run, silent.Player).Take(3).Select(p => p.Id));
        var locked = NewCombat(unlocks: UnlockProfile.None);
        var lockedOptions = PotionFactory.GetPotionOptions(locked.Run, locked.Player).Select(p => p.Id).ToList();
        Assert.Equal(39, lockedOptions.Count);
        Assert.Equal("ATTACK_POTION", lockedOptions[0]);
    }

    public static void Test_RelicPotion_PotionFactoryDrawsOneFloatThenOneItemPerPick()
    {
        var seen = new HashSet<PotionRarity>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var cs = NewCombat();
            var options = PotionFactory.GetPotionOptions(cs.Run, cs.Player).ToList();
            var rng = new Rng(seed);
            var mirror = new Rng(seed);
            var picks = PotionFactory.CreateRandomPotionsOutOfCombat(cs.Run, cs.Player, 3, rng).ToList();
            var remaining = new List<CanonicalPotion>(options);
            var expected = new List<CanonicalPotion>();
            for (int i = 0; i < 3; i++)
            {
                float roll = mirror.NextFloat();
                var rarity = roll <= 0.1f ? PotionRarity.Rare : roll <= 0.35f ? PotionRarity.Uncommon : PotionRarity.Common;
                var candidates = remaining.Where(p => p.Rarity == rarity).ToList();
                var pick = candidates[mirror.NextInt(0, candidates.Count)];
                expected.Add(pick);
                remaining.Remove(pick);
                seen.Add(rarity);
            }
            Assert.SequenceEqual(expected.Select(p => p.Id), picks.Select(p => p.Id), $"seed {seed}");
            Assert.Equal(6, rng.Counter, $"seed {seed}: one float and one item per pick");
            Assert.Equal(3, picks.Distinct().Count(), $"seed {seed}: a pick leaves the options");
            var single = PotionFactory.CreateRandomPotionOutOfCombat(cs.Run, cs.Player, new Rng(seed));
            Assert.Equal(picks[0].Id, single.Id, $"seed {seed}: the single draw is the first of the batch");
        }
        Assert.Equal(3, seen.Count, "the seeds roll every rarity");
    }

    public static void Test_RelicPotion_PotionRarityThresholds()
    {
        Assert.Equal(0.1f, PotionFactory.RareChance);
        Assert.Equal(0.1f, PotionFactory.RareThreshold);
        Assert.Equal(0.25f, PotionFactory.UncommonChance);
        Assert.Equal(0.35f, PotionFactory.UncommonThreshold);
        var counts = new Dictionary<PotionRarity, int>();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var cs = NewCombat();
            float roll = new Rng(seed).NextFloat();
            var expected = roll <= 0.1f ? PotionRarity.Rare : roll <= 0.35f ? PotionRarity.Uncommon : PotionRarity.Common;
            var pick = PotionFactory.CreateRandomPotionOutOfCombat(cs.Run, cs.Player, new Rng(seed));
            Assert.Equal(expected, pick.Rarity, $"seed {seed} roll {roll}");
            counts[expected] = counts.GetValueOrDefault(expected) + 1;
        }
        Assert.True(counts.GetValueOrDefault(PotionRarity.Rare) > 0 && counts.GetValueOrDefault(PotionRarity.Uncommon) > 0 && counts.GetValueOrDefault(PotionRarity.Common) > 0);
    }

    public static void Test_RelicPotion_InCombatPotionsSkipThoseThatCannotBeGeneratedInCombat()
    {
        var excluded = new[] { "FAIRY_IN_A_BOTTLE", "FRUIT_JUICE", "REGEN_POTION" };
        var blacklist = new[] { PotionPools.Canonical("BLOOD_POTION"), PotionPools.Canonical("ATTACK_POTION") };
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var cs = NewCombat();
            var options = PotionFactory.GetPotionOptions(cs.Run, cs.Player).Where(p => p.CanBeGeneratedInCombat).Except(blacklist).ToList();
            var mirror = new Rng(seed);
            float roll = mirror.NextFloat();
            var rarity = roll <= 0.1f ? PotionRarity.Rare : roll <= 0.35f ? PotionRarity.Uncommon : PotionRarity.Common;
            var candidates = options.Where(p => p.Rarity == rarity).ToList();
            var expected = candidates[mirror.NextInt(0, candidates.Count)];
            var rng = new Rng(seed);
            var pick = PotionFactory.CreateRandomPotionInCombat(cs.Run, cs.Player, rng, blacklist);
            Assert.Equal(expected.Id, pick.Id, $"seed {seed}");
            Assert.False(excluded.Contains(pick.Id), $"seed {seed}: {pick.Id} cannot be generated in combat");
            Assert.False(pick.Id is "BLOOD_POTION" or "ATTACK_POTION", $"seed {seed}: blacklisted");
            Assert.Equal(2, rng.Counter);
        }
    }

    public static void Test_RelicPotion_AnExhaustedRarityYieldsTheGamesNullPick()
    {
        var cs = NewCombat();
        var rares = PotionFactory.GetPotionOptions(cs.Run, cs.Player).Where(p => p.Rarity == PotionRarity.Rare).ToList();
        ulong seed = 1;
        while (new Rng(seed).NextFloat() > 0.1f)
        {
            seed++;
        }
        var rng = new Rng(seed);
        var pick = PotionFactory.CreateRandomPotionOutOfCombat(cs.Run, cs.Player, rng, rares);
        Assert.Null(pick, "NextItem over no option returns null and the game adds it (Factories/PotionFactory.cs:56-58)");
        Assert.Equal(1, rng.Counter, "an empty item set draws nothing");
    }

    public static void Test_RelicPotion_IsValidTargetTruthTable()
    {
        var cs = NewCombat(monsters: 2);
        var owner = cs.Player;
        var enemy = cs.Monsters[0];
        var deadEnemy = cs.Monsters[1];
        deadEnemy.CurrentHp = 0;
        var ally = new MonsterState(10, new MonsterDouble(), 10, 10, null, null) { Side = CombatSide.Player };
        var otherPlayer = new PlayerState(11, StartingDecks.IroncladCharacterId, 10, 10, 3);
        var targets = new (string Name, CreatureState? Target)[] { ("null", null), ("owner", owner), ("enemy", enemy), ("dead enemy", deadEnemy), ("ally", ally), ("other player", otherPlayer) };
        var table = new (TargetType Type, bool[] Valid)[]
        {
            (TargetType.None, new[] { true, false, false, false, false, false }),
            (TargetType.Self, new[] { false, true, false, false, false, false }),
            (TargetType.AnyEnemy, new[] { false, false, true, false, false, false }),
            (TargetType.AllEnemies, new[] { true, false, false, false, false, false }),
            (TargetType.RandomEnemy, new[] { true, false, false, false, false, false }),
            (TargetType.AnyPlayer, new[] { false, true, false, false, false, true }),
            (TargetType.AnyAlly, new[] { false, false, false, false, true, true }),
            (TargetType.AllAllies, new[] { true, false, false, false, false, false }),
            (TargetType.TargetedNoCreature, new[] { true, false, false, false, false, false }),
            (TargetType.Osty, new[] { true, false, false, false, false, false })
        };
        foreach (var (type, valid) in table)
        {
            var potion = new TestPotion(targetType: type);
            potion.AttachTo(owner);
            for (int i = 0; i < targets.Length; i++)
            {
                Assert.Equal(valid[i], potion.IsValidTarget(targets[i].Target), $"{type} -> {targets[i].Name}");
            }
        }
        owner.CurrentHp = 0;
        var self = new TestPotion(targetType: TargetType.Self);
        self.AttachTo(owner);
        Assert.False(self.IsValidTarget(owner), "a dead owner is no valid Self target");
    }

    public static void Test_RelicPotion_SingleTargetTypesFollowActionTargetExtensions()
    {
        var single = new[] { TargetType.Self, TargetType.AnyEnemy, TargetType.AnyPlayer, TargetType.AnyAlly, TargetType.TargetedNoCreature };
        foreach (var type in Enum.GetValues<TargetType>())
        {
            Assert.Equal(single.Contains(type), PotionModel.IsSingleTarget(type), type.ToString());
        }
    }

    public static void Test_RelicPotion_ManualUseTargetDefaultsToTheOwnerOnlyWhenValid()
    {
        var cs = NewCombat();
        var enemy = cs.Monsters[0];
        PotionModel Make(TargetType type)
        {
            var potion = new TestPotion(targetType: type);
            potion.AttachTo(cs.Player);
            return potion;
        }
        Assert.True(ReferenceEquals(cs.Player, Make(TargetType.Self).ManualUseTarget(null)));
        Assert.True(ReferenceEquals(cs.Player, Make(TargetType.AnyPlayer).ManualUseTarget(null)));
        Assert.Null(Make(TargetType.AnyEnemy).ManualUseTarget(null));
        Assert.Null(Make(TargetType.AllEnemies).ManualUseTarget(null));
        Assert.Null(Make(TargetType.TargetedNoCreature).ManualUseTarget(null));
        Assert.True(ReferenceEquals(enemy, Make(TargetType.AnyEnemy).ManualUseTarget(enemy)));
        Assert.True(ReferenceEquals(enemy, Make(TargetType.Self).ManualUseTarget(enemy)), "an explicit target is kept");
    }

    public static void Test_RelicPotion_ResolveTargetFollowsUsePotionAction()
    {
        var cs = NewCombat();
        var enemy = cs.Monsters[0];
        var otherPlayer = new PlayerState(11, StartingDecks.IroncladCharacterId, 10, 10, 3);
        PotionModel Make(TargetType type)
        {
            var potion = new TestPotion(targetType: type);
            potion.AttachTo(cs.Player);
            return potion;
        }
        Assert.True(ReferenceEquals(cs.Player, PotionActions.ResolveTarget(cs, Make(TargetType.Self), null)));
        Assert.True(ReferenceEquals(cs.Player, PotionActions.ResolveTarget(cs, Make(TargetType.AnyEnemy), null)), "a single-target type without a target id resolves to the owner");
        Assert.Null(PotionActions.ResolveTarget(cs, Make(TargetType.AllEnemies), null));
        Assert.True(ReferenceEquals(enemy, PotionActions.ResolveTarget(cs, Make(TargetType.AnyEnemy), enemy)));
        Assert.True(ReferenceEquals(enemy, PotionActions.ResolveTarget(cs, Make(TargetType.Self), enemy)), "a target id is used as given");
        cs.IsInProgress = false;
        Assert.True(ReferenceEquals(cs.Player, PotionActions.ResolveTarget(cs, Make(TargetType.Self), null)));
        Assert.Null(PotionActions.ResolveTarget(cs, Make(TargetType.TargetedNoCreature), null));
        Assert.True(ReferenceEquals(otherPlayer, PotionActions.ResolveTarget(cs, Make(TargetType.AnyPlayer), otherPlayer)));
        Assert.True(ReferenceEquals(cs.Player, PotionActions.ResolveTarget(cs, Make(TargetType.AnyPlayer), enemy)), "out of combat a creature that is no player gives no target player id");
    }

    public static void Test_RelicPotion_CanUseTruthTable()
    {
        var cs = NewCombat(monsters: 2);
        var enemy = cs.Monsters[0];
        cs.Monsters[1].CurrentHp = 0;
        Assert.False(PotionActions.CanUse(cs, -1, null), "slot below range");
        Assert.False(PotionActions.CanUse(cs, cs.Player.PotionSlots.Length, null), "slot above range");
        Assert.False(PotionActions.CanUse(cs, 0, null), "empty slot");
        var self = Slot(cs, new TestPotion(PotionUsage.CombatOnly, TargetType.Self), 0);
        Assert.True(PotionActions.CanUse(cs, 0, null), "Self without a target resolves to the owner");
        Assert.True(PotionActions.CanUse(cs, 0, cs.Player));
        Assert.False(PotionActions.CanUse(cs, 0, enemy), "an enemy is no Self target");
        cs.CurrentSide = CombatSide.Enemy;
        Assert.False(PotionActions.CanUse(cs, 0, null), "CombatOnly needs the owner's side");
        var anyTime = Slot(cs, new TestPotion(PotionUsage.AnyTime, TargetType.Self), 1);
        Assert.True(PotionActions.CanUse(cs, 1, null), "AnyTime ignores the side");
        cs.CurrentSide = CombatSide.Player;
        cs.IsInProgress = false;
        Assert.False(PotionActions.CanUse(cs, 0, null), "CombatOnly needs a combat in progress");
        Assert.True(PotionActions.CanUse(cs, 1, null), "AnyTime is enabled outside combat");
        cs.IsInProgress = true;
        var automatic = Slot(cs, new TestPotion(PotionUsage.Automatic, TargetType.Self), 2);
        Assert.False(PotionActions.CanUse(cs, 2, null));
        Assert.False(PotionActions.CanUse(cs, 2, cs.Player));
        cs.Player.PotionSlots[2] = null;
        Slot(cs, new TestPotion(PotionUsage.None, TargetType.Self), 2);
        Assert.Throws<InvalidOperationException>(() => PotionActions.CanUse(cs, 2, null), "None usage is invalid");
        cs.Player.PotionSlots[2] = null;
        self.CustomUsable = false;
        Assert.False(PotionActions.CanUse(cs, 0, null), "PassesCustomUsabilityCheck");
        self.CustomUsable = true;
        cs.Player.CanUseOrRemovePotions = false;
        Assert.False(PotionActions.CanUse(cs, 0, null), "CanUseOrRemovePotions disables use");
        Assert.False(PotionActions.CanUse(cs, 1, null), "CanUseOrRemovePotions disables AnyTime use");
        cs.Player.CanUseOrRemovePotions = true;
        cs.Player.PotionSlots[1] = null;
        var anyEnemy = Slot(cs, new TestPotion(PotionUsage.CombatOnly, TargetType.AnyEnemy), 1);
        Assert.False(PotionActions.CanUse(cs, 1, null), "AnyEnemy without a target resolves to the owner, which is invalid");
        Assert.True(PotionActions.CanUse(cs, 1, enemy));
        Assert.False(PotionActions.CanUse(cs, 1, cs.Monsters[1]), "a dead enemy");
        cs.Player.PotionSlots[2] = null;
        Slot(cs, new TestPotion(PotionUsage.CombatOnly, TargetType.AllEnemies), 2);
        Assert.True(PotionActions.CanUse(cs, 2, null));
        cs.Player.CurrentHp = 0;
        Assert.False(PotionActions.CanUse(cs, 0, null), "a dead owner");
        Assert.True(anyTime.HasOwner && anyEnemy.HasOwner && automatic.HasOwner);
    }

    public static void Test_RelicPotion_CanUseOfThePilotPotions()
    {
        var cs = NewCombat();
        var enemy = cs.Monsters[0];
        Slot(cs, new FairyInABottle(), 0);
        Slot(cs, new StrengthPotion(), 1);
        Slot(cs, new FirePotion(), 2);
        Assert.False(PotionActions.CanUse(cs, 0, null), "Automatic");
        Assert.False(PotionActions.CanUse(cs, 0, cs.Player), "Automatic");
        Assert.True(PotionActions.CanDiscard(cs, 0), "an Automatic potion can be discarded");
        Assert.True(PotionActions.CanUse(cs, 1, null));
        Assert.True(PotionActions.CanUse(cs, 1, cs.Player));
        Assert.False(PotionActions.CanUse(cs, 1, enemy), "AnyPlayer");
        Assert.True(PotionActions.CanUse(cs, 2, enemy));
        Assert.False(PotionActions.CanUse(cs, 2, null));
        Assert.False(PotionActions.CanUse(cs, 2, cs.Player));
    }

    public static void Test_RelicPotion_CanDiscardTruthTable()
    {
        var cs = NewCombat();
        Assert.False(PotionActions.CanDiscard(cs, -1));
        Assert.False(PotionActions.CanDiscard(cs, 3));
        Assert.False(PotionActions.CanDiscard(cs, 0), "empty slot");
        Slot(cs, new TestPotion(), 0);
        Assert.True(PotionActions.CanDiscard(cs, 0));
        cs.CurrentSide = CombatSide.Enemy;
        Assert.True(PotionActions.CanDiscard(cs, 0), "discard has no side gate");
        cs.Player.CanUseOrRemovePotions = false;
        Assert.False(PotionActions.CanDiscard(cs, 0));
        cs.Player.CanUseOrRemovePotions = true;
        cs.Player.CurrentHp = 0;
        Assert.False(PotionActions.CanDiscard(cs, 0), "a dead owner");
    }

    public static void Test_RelicPotion_UsePotionCancelPathsLeaveTheStateUnchanged()
    {
        var cs = NewCombat(monsters: 2);
        cs.Monsters[1].CurrentHp = 0;
        ulong empty = Hash(cs);
        PotionActions.UsePotion(cs, 0, null);
        Assert.Equal(empty, Hash(cs), "an empty slot cancels");
        var log = new List<string>();
        var anyEnemy = Slot(cs, new TestPotion(PotionUsage.CombatOnly, TargetType.AnyEnemy, log), 0);
        ulong before = Hash(cs);
        PotionActions.UsePotion(cs, 0, null);
        PotionActions.UsePotion(cs, 0, cs.Monsters[1]);
        Assert.Equal(before, Hash(cs), "an invalid target cancels");
        Assert.True(ReferenceEquals(anyEnemy, cs.Player.PotionSlots[0]));
        Assert.Equal(0, log.Count);
        Assert.Throws<IndexOutOfRangeException>(() => PotionActions.UsePotion(cs, 3, null));
        Assert.Throws<IndexOutOfRangeException>(() => PotionActions.UsePotion(cs, -1, null));
        Assert.Equal(0, cs.Choice.ContextDepth, "no choice context is left behind");
    }

    public static void Test_RelicPotion_DiscardPotionActionBoundsAndEmptySlot()
    {
        var cs = NewCombat();
        Assert.Throws<IndexOutOfRangeException>(() => PotionActions.DiscardPotion(cs, 3));
        Assert.Throws<IndexOutOfRangeException>(() => PotionActions.DiscardPotion(cs, -1));
        ulong before = Hash(cs);
        PotionActions.DiscardPotion(cs, 1);
        Assert.Equal(before, Hash(cs), "an empty slot cancels");
        Assert.Null(PotionActions.GetPotionAtSlotIndex(cs.Player, 2));
        Assert.Throws<IndexOutOfRangeException>(() => PotionActions.GetPotionAtSlotIndex(cs.Player, 3));
    }

    public static void Test_RelicPotion_PotionInternalsDiscardAndRemoveBeforeUse()
    {
        var cs = NewCombat();
        var a = Slot(cs, new TestPotion(), 0);
        var b = Slot(cs, new TestPotion(), 2);
        a.Discard();
        Assert.Null(cs.Player.PotionSlots[0]);
        Assert.True(a.HasBeenRemovedFromState);
        Assert.True(ReferenceEquals(cs.Player, a.Owner), "the owner is kept");
        b.RemoveBeforeUse();
        Assert.Null(cs.Player.PotionSlots[2]);
        Assert.True(b.HasBeenRemovedFromState);
        Assert.Throws<InvalidOperationException>(() => a.Discard(), "a potion that left its slot");
        Assert.Throws<InvalidOperationException>(() => new TestPotion().Discard(), "an unowned potion");
        var other = new PlayerState(5, StartingDecks.IroncladCharacterId, 10, 10, 3);
        Assert.Throws<InvalidOperationException>(() => a.AttachTo(other), "a potion cannot change owner");
        a.AttachTo(cs.Player);
    }

    public static void Test_RelicPotion_CanThrowAtAllyNeedsTwoPlayersInCombat()
    {
        var cs = NewCombat();
        var anyPlayer = Slot(cs, new TestPotion(targetType: TargetType.AnyPlayer), 0);
        var self = Slot(cs, new TestPotion(targetType: TargetType.Self), 1);
        Assert.False(anyPlayer.CanThrowAtAlly(cs.Run), "single player");
        cs.Run.Players.Add(new PlayerState(7, StartingDecks.IroncladCharacterId, 10, 10, 3));
        Assert.True(anyPlayer.CanThrowAtAlly(cs.Run));
        Assert.False(self.CanThrowAtAlly(cs.Run));
        cs.IsInProgress = false;
        Assert.False(anyPlayer.CanThrowAtAlly(cs.Run));
    }

    public static void Test_RelicPotion_OnUseWrapperRemovesThePotionBeforeAnyHook()
    {
        var cs = NewCombat();
        var potion = Slot(cs, new TestPotion(), 1);
        try
        {
            potion.OnUseWrapper(cs.Run, cs.Player);
        }
        catch (NotImplementedException)
        {
        }
        Assert.Null(cs.Player.PotionSlots[1]);
        Assert.True(potion.HasBeenRemovedFromState);
        Assert.True(potion.HasOwner);
        Assert.Equal(0, cs.CardOrPotionEffectDepth);
    }

    public static void Test_RelicPotion_OnUseWrapperRunsTheTracedOrder()
    {
        W1Pending.Run(nameof(Test_RelicPotion_OnUseWrapperRunsTheTracedOrder), Hooks, () =>
        {
            var cs = NewCombat();
            var log = new List<string>();
            new RecordingPower(log).ApplyInternal(cs.Player, 1);
            var def = new CardDouble("RELIC_POTION_SKILL", CardType.Skill);
            AddCard(cs, cs.Player.Hand, def);
            var first = Slot(cs, new TestPotion(log: log), 0);
            first.OnUseWrapper(cs.Run, cs.Player);
            Assert.SequenceEqual(
                new[]
                {
                    "before-used:RELIC_POTION_TEST_POTION:removed=True:depth=0",
                    "on-use:0",
                    "after-used:RELIC_POTION_TEST_POTION:depth=0:history=1"
                },
                log);
            Assert.Equal(1, first.DepthDuringOnUse, "BeginCardOrPotionEffect brackets OnUse");
            Assert.Equal(0, cs.CardOrPotionEffectDepth);
            var entry = cs.History.PotionUsedEntries[0];
            Assert.Equal("RELIC_POTION_TEST_POTION", entry.PotionId);
            Assert.Equal(cs.Player.CombatId, entry.TargetId);

            log.Clear();
            cs.Player.Hand.ClearInternal();
            Slot(cs, new TestPotion(log: log), 1).OnUseWrapper(cs.Run, cs.Player);
            Assert.SequenceEqual(
                new[]
                {
                    "before-used:RELIC_POTION_TEST_POTION:removed=True:depth=0",
                    "on-use:0",
                    "after-used:RELIC_POTION_TEST_POTION:depth=0:history=2",
                    "hand-emptied"
                },
                log);

            log.Clear();
            cs.CardOrPotionEffectDepth = 1;
            Slot(cs, new TestPotion(log: log), 2).OnUseWrapper(cs.Run, cs.Player);
            Assert.Equal(1, cs.CardOrPotionEffectDepth, "the enclosing effect keeps its depth");
            Assert.False(log.Contains("hand-emptied"), "no AfterHandEmptied inside an enclosing card or potion effect");
            cs.CardOrPotionEffectDepth = 0;

            log.Clear();
            var killer = Slot(cs, new TestPotion(log: log), 0);
            killer.Effect = (rs, target) => cs.Player.CurrentHp = 0;
            killer.OnUseWrapper(cs.Run, cs.Player);
            Assert.SequenceEqual(new[] { "before-used:RELIC_POTION_TEST_POTION:removed=True:depth=0", "on-use:0" }, log, "a dead owner skips history, AfterPotionUsed and the hand check");
            Assert.Equal(3, cs.History.PotionUsedEntries.Count);
            Assert.Equal(0, cs.CardOrPotionEffectDepth);
            cs.Player.CurrentHp = 80;

            log.Clear();
            var thrower = Slot(cs, new TestPotion(log: log), 1);
            thrower.Effect = (rs, target) => throw new InvalidOperationException("boom");
            var ex = Assert.Throws<InvalidOperationException>(() => thrower.OnUseWrapper(cs.Run, cs.Player));
            Assert.Equal("boom", ex.Message);
            Assert.Equal(0, cs.CardOrPotionEffectDepth, "EndCardOrPotionEffect runs before the rethrow");
            Assert.False(log.Exists(l => l.StartsWith("after-used", StringComparison.Ordinal)));
        });
    }

    public static void Test_RelicPotion_UsedPotionIsNotItsOwnListener()
    {
        W1Pending.Run(nameof(Test_RelicPotion_UsedPotionIsNotItsOwnListener), Hooks, () =>
        {
            var cs = NewCombat();
            var log = new List<string>();
            var watcher = Slot(cs, new SelfWatchingPotion(log, 0), 0);
            var other = Slot(cs, new SelfWatchingPotion(log, 1), 1);
            RequireListeners(cs, watcher, other);
            watcher.OnUseWrapper(cs.Run, cs.Player);
            Assert.SequenceEqual(new[] { "1:before", "1:after" }, log, "only the potion still in its slot listens (Combat/CombatState.cs:1057)");
        });
    }

    private sealed class SelfWatchingPotion : PotionModel
    {
        private readonly List<string> _log;
        private readonly int _tag;

        public SelfWatchingPotion(List<string> log, int tag)
        {
            _log = log;
            _tag = tag;
        }

        public override string Id => "RELIC_POTION_SELF_WATCHING_POTION";

        public override PotionRarity Rarity => PotionRarity.Common;

        public override PotionUsage Usage => PotionUsage.CombatOnly;

        public override TargetType TargetType => TargetType.Self;

        public override void BeforePotionUsed(RunState rs, PotionModel potion, CreatureState? target) => _log.Add(_tag + ":before");

        public override void AfterPotionUsed(RunState rs, PotionModel potion, CreatureState? target) => _log.Add(_tag + ":after");
    }

    public static void Test_RelicPotion_RelicsAndPotionSlotsAreCombatListenersInGameOrder()
    {
        W1Pending.Run(nameof(Test_RelicPotion_RelicsAndPotionSlotsAreCombatListenersInGameOrder), Hooks, () =>
        {
            var cs = NewCombat();
            var rs = cs.Run;
            var playerPower = new RecordingPower(new List<string>());
            playerPower.ApplyInternal(cs.Player, 1);
            var monsterPower = new RecordingPower(new List<string>());
            monsterPower.ApplyInternal(cs.Monsters[0], 1);
            var a = RelicCmd.Obtain(rs, new TestRelic("A"), cs.Player);
            var melted = RelicCmd.Obtain(rs, new TestRelic("B") { IsWax = true }, cs.Player);
            RelicCmd.Melt(rs, melted);
            var c = RelicCmd.Obtain(rs, new TestRelic("C"), cs.Player);
            var p0 = Slot(cs, new TestPotion(), 0);
            var p2 = Slot(cs, new TestPotion(), 2);
            RequireListeners(cs, a, p0);
            var buffer = new List<AbstractModel>();
            Hook.IterateHookListeners(cs, buffer);
            Assert.SequenceEqual(new AbstractModel[] { playerPower, a, c, p0, p2, monsterPower }, buffer, "player powers, unmelted relics, potion slots, then the enemies");
            cs.Player.DeactivateHooks();
            Hook.IterateHookListeners(cs, buffer);
            Assert.SequenceEqual(new AbstractModel[] { monsterPower }, buffer, "an inactive player stops after its powers (Combat/CombatState.cs:870-873), and those powers are not live at yield (:1061)");
            cs.Player.ActivateHooks();
            RelicCmd.Remove(rs, c);
            Hook.IterateHookListeners(cs, buffer);
            Assert.False(buffer.Contains(c), "a removed relic is no listener");
        });
    }

    public static void Test_RelicPotion_TryToProcureFillsTheFirstEmptySlot()
    {
        W1Pending.Run(nameof(Test_RelicPotion_TryToProcureFillsTheFirstEmptySlot), Hooks, () =>
        {
            var cs = NewCombat();
            var rs = cs.Run;
            var log = new List<string>();
            var gate = new RecordingPower(log);
            gate.ApplyInternal(cs.Player, 1);
            var a = new TestPotion();
            var result = PotionCmd.TryToProcure(rs, a, cs.Player);
            Assert.True(result.Success);
            Assert.True(ReferenceEquals(a, result.Potion));
            Assert.Equal(PotionProcureFailureReason.None, result.FailureReason);
            Assert.True(ReferenceEquals(a, cs.Player.PotionSlots[0]));
            Assert.True(ReferenceEquals(cs.Player, a.Owner));
            Assert.SequenceEqual(new[] { "should-procure:RELIC_POTION_TEST_POTION", "procured:RELIC_POTION_TEST_POTION" }, log);
            var b = new TestPotion();
            Assert.True(PotionCmd.TryToProcure(rs, b, cs.Player, 2).Success);
            Assert.True(ReferenceEquals(b, cs.Player.PotionSlots[2]));
            var occupied = PotionCmd.TryToProcure(rs, new TestPotion(), cs.Player, 2);
            Assert.False(occupied.Success);
            Assert.Equal(PotionProcureFailureReason.TooFull, occupied.FailureReason);
            var strength = PotionCmd.TryToProcure(rs, StrengthPotion.PotionId, cs.Player);
            Assert.True(strength.Success);
            Assert.True(cs.Player.PotionSlots[1] is StrengthPotion);
            log.Clear();
            var full = new TestPotion();
            var tooFull = PotionCmd.TryToProcure(rs, full, cs.Player);
            Assert.False(tooFull.Success);
            Assert.Equal(PotionProcureFailureReason.TooFull, tooFull.FailureReason);
            Assert.False(full.HasOwner);
            Assert.SequenceEqual(new[] { "should-procure:RELIC_POTION_TEST_POTION" }, log, "no AfterPotionProcured without a slot");
            cs.Player.PotionSlots[0] = null;
            gate.AllowProcure = false;
            log.Clear();
            var refused = PotionCmd.TryToProcure(rs, new TestPotion(), cs.Player);
            Assert.False(refused.Success);
            Assert.Equal(PotionProcureFailureReason.NotAllowed, refused.FailureReason);
            Assert.Null(cs.Player.PotionSlots[0]);
            Assert.SequenceEqual(new[] { "should-procure:RELIC_POTION_TEST_POTION" }, log);
        });
    }

    public static void Test_RelicPotion_DiscardEmptiesTheSlotBeforeTheHook()
    {
        W1Pending.Run(nameof(Test_RelicPotion_DiscardEmptiesTheSlotBeforeTheHook), Hooks, () =>
        {
            var cs = NewCombat();
            var log = new List<string>();
            new RecordingPower(log).ApplyInternal(cs.Player, 1);
            var potion = Slot(cs, new TestPotion(), 1);
            PotionActions.DiscardPotion(cs, 1);
            Assert.Null(cs.Player.PotionSlots[1]);
            Assert.True(potion.HasBeenRemovedFromState);
            Assert.SequenceEqual(new[] { "discarded:RELIC_POTION_TEST_POTION:removed=True" }, log);
            Assert.Throws<InvalidOperationException>(() => PotionCmd.Discard(cs.Run, potion), "a potion that left its slot");
            Assert.Equal(0, cs.History.PotionUsedEntries.Count, "discarding records no use");
        });
    }

    public static void Test_RelicPotion_RelicCmdObtainRemoveReplaceMelt()
    {
        var cs = NewCombat();
        var rs = cs.Run;
        var log = new List<string>();
        var a = new TestRelic("A", log: log);
        var b = new TestRelic("B", log: log);
        var c = new TestRelic("C", log: log);
        Assert.True(ReferenceEquals(a, RelicCmd.Obtain(rs, a, cs.Player)));
        Assert.True(ReferenceEquals(cs.Player, a.Owner));
        Assert.False(rs.Fidelity.Entries.Contains(new FidelityEntry(FidelityFlag.ApproximatedRule, RelicCmd.ObtainRunLayerSite)), "E3: the grab-bag removals and FloorAddedToDeck are ported, no approximation flag");
        Assert.Equal(rs.TotalFloor, a.FloorAddedToDeck);
        RelicCmd.Obtain(rs, b, cs.Player);
        RelicCmd.Obtain(rs, c, cs.Player, 0);
        Assert.SequenceEqual(new RelicModel[] { c, a, b }, cs.Player.Relics);
        Assert.SequenceEqual(new[] { "A:obtained", "B:obtained", "C:obtained" }, log);
        RelicCmd.Remove(rs, a);
        Assert.SequenceEqual(new RelicModel[] { c, b }, cs.Player.Relics);
        Assert.True(a.HasBeenRemovedFromState);
        Assert.True(ReferenceEquals(cs.Player, a.Owner), "the owner is kept");
        Assert.Equal("A:removed", log[^1]);
        Assert.Throws<InvalidOperationException>(() => RelicCmd.Remove(rs, a));
        var d = new TestRelic("D", log: log);
        Assert.True(ReferenceEquals(d, RelicCmd.Replace(rs, b, d)));
        Assert.SequenceEqual(new RelicModel[] { c, d }, cs.Player.Relics, "the replacement takes the original's index");
        Assert.SequenceEqual(new[] { "B:removed", "D:obtained" }, log.Skip(log.Count - 2));
        var notWax = Assert.Throws<InvalidOperationException>(() => RelicCmd.Melt(rs, c));
        Assert.True(notWax.Message.Contains("not wax", StringComparison.Ordinal));
        c.IsWax = true;
        RelicCmd.Melt(rs, c);
        Assert.True(c.IsMelted);
        Assert.Equal(RelicStatus.Disabled, c.Status);
        Assert.True(ReferenceEquals(c, cs.Player.Relics[0]), "a melted relic stays in the list");
        Assert.True(ReferenceEquals(c, cs.Player.GetRelic("C")), "GetRelic does not skip melted relics");
        Assert.Equal("C:removed", log[^1]);
        Assert.Throws<InvalidOperationException>(() => RelicCmd.Melt(rs, c));
        var flower = RelicCmd.Obtain(rs, HappyFlower.RelicId, cs.Player);
        Assert.True(flower is HappyFlower && ReferenceEquals(cs.Player, flower.Owner));
        var stranger = new PlayerState(9, StartingDecks.IroncladCharacterId, 10, 10, 3);
        Assert.Throws<InvalidOperationException>(() => RelicCmd.Obtain(rs, d, stranger), "a relic cannot change owner");
        ulong hash = Hash(cs);
        var clone = cs.DeepClone();
        Assert.Equal(hash, Hash(clone));
        RelicCmd.Remove(clone.Run, clone.Player.Relics[1]);
        Assert.Equal(hash, Hash(cs), "removing from the clone leaves the source");
        Assert.NotEqual(hash, Hash(clone));
    }

    public static void Test_RelicPotion_CloneAndCanonicalCoverRelicAndPotionState()
    {
        var cs = NewCombat();
        Equip(cs, new HappyFlower());
        Equip(cs, new CentennialPuzzle());
        Equip(cs, new Nunchaku());
        Equip(cs, new TestRelic("STACK") { Stackable = true });
        Slot(cs, new StrengthPotion(), 0);
        Slot(cs, new FairyInABottle(), 2);
        ulong hash = Hash(cs);
        var clone = cs.DeepClone();
        Assert.Equal(hash, Hash(clone));
        for (int i = 0; i < cs.Player.Relics.Count; i++)
        {
            Assert.False(ReferenceEquals(cs.Player.Relics[i], clone.Player.Relics[i]));
            Assert.True(ReferenceEquals(clone.Player, clone.Player.Relics[i].Owner));
            Assert.False(ReferenceEquals(cs.Player.Relics[i].DynamicVars, clone.Player.Relics[i].DynamicVars) && cs.Player.Relics[i].DynamicVars.Count > 0, "dynamic vars are per instance");
            for (int v = 0; v < cs.Player.Relics[i].DynamicVars.Count; v++)
            {
                Assert.False(ReferenceEquals(cs.Player.Relics[i].DynamicVars.All[v], clone.Player.Relics[i].DynamicVars.All[v]), "each var is copied");
            }
        }
        Assert.False(ReferenceEquals(cs.Player.PotionSlots[0]!.DynamicVars.All[0], clone.Player.PotionSlots[0]!.DynamicVars.All[0]), "potion vars are copied");
        var testRelic = (TestRelic)clone.Player.Relics[3];
        testRelic.Log.Add("clone only");
        Assert.Equal(0, ((TestRelic)cs.Player.Relics[3]).Log.Count, "CopyMutableReferenceState runs for subclasses");
        Assert.True(ReferenceEquals(clone.Player, clone.Player.PotionSlots[0]!.Owner));
        var mutations = new (string Name, Action<CombatState> Mutate)[]
        {
            ("IsWax", s => s.Player.Relics[0].IsWax = true),
            ("IsMelted", s => s.Player.Relics[0].IsMelted = true),
            ("StackCount", s => s.Player.Relics[3].IncrementStackCount()),
            ("FloorAddedToDeck", s => s.Player.Relics[0].FloorAddedToDeck = 7),
            ("Status", s => s.Player.Relics[0].Status = RelicStatus.Active),
            ("HasBeenRemovedFromState", s => s.Player.Relics[0].RemoveInternal()),
            ("relic dynamic var", s => s.Player.Relics[0].DynamicVars[HappyFlower.TurnsKey].BaseValue = 4m),
            ("TurnsSeen", s => ((HappyFlower)s.Player.Relics[0]).TurnsSeen = 2),
            ("UsedThisCombat", s => s.Player.Relics[1].AfterDamageReceived(s.Run, s.Player, new DamageResult(s.Player, ValueProp.Move) { UnblockedDamage = 1 }, ValueProp.Move, null, null)),
            ("AttacksPlayed", s => ((Nunchaku)s.Player.Relics[2]).AttacksPlayed = 3),
            ("relic order", s => s.Player.Relics.Reverse()),
            ("potion removed", s => s.Player.PotionSlots[0]!.RemoveBeforeUse()),
            ("potion dynamic var", s => s.Player.PotionSlots[0]!.DynamicVars.Strength.BaseValue = 3m),
            ("potion slot move", s =>
            {
                s.Player.PotionSlots[1] = s.Player.PotionSlots[0];
                s.Player.PotionSlots[0] = null;
            })
        };
        foreach (var (name, mutate) in mutations)
        {
            var probe = cs.DeepClone();
            mutate(probe);
            Assert.NotEqual(hash, Hash(probe), $"{name} must reach the canonical form");
            Assert.Equal(hash, Hash(cs), $"mutating {name} on a clone changed the source");
            Assert.Equal(hash, Hash(clone), $"mutating {name} on a clone changed a sibling");
        }
    }

    public static void Test_RelicPotion_RegisteredPilotsMatchTheDecompile()
    {
        var relics = new (string Id, RelicRarity Rarity, string Pool, bool Counter)[]
        {
            (HappyFlower.RelicId, RelicRarity.Common, RelicPools.SharedRelicPool, true),
            (CentennialPuzzle.RelicId, RelicRarity.Common, RelicPools.SharedRelicPool, false),
            (Whetstone.RelicId, RelicRarity.Common, RelicPools.SharedRelicPool, false),
            (GamblingChip.RelicId, RelicRarity.Rare, RelicPools.SharedRelicPool, false),
            (PaperPhrog.RelicId, RelicRarity.Uncommon, RelicPools.IroncladRelicPool, false),
            (PaperKrane.RelicId, RelicRarity.Rare, RelicPools.SilentRelicPool, false),
            (Nunchaku.RelicId, RelicRarity.Uncommon, RelicPools.SharedRelicPool, true)
        };
        foreach (var (id, rarity, pool, counter) in relics)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id);
            Assert.Equal(rarity, relic.Rarity, id);
            Assert.Equal(pool, relic.Pool, id);
            Assert.Equal(counter, relic.ShowCounter, id);
        }
        Assert.True(RelicRegistry.Count >= 6);
        var flower = new HappyFlower();
        Assert.Equal(1m, flower.DynamicVars.Energy.BaseValue);
        Assert.Equal(3m, flower.DynamicVars[HappyFlower.TurnsKey].BaseValue);
        Assert.Equal(3m, new CentennialPuzzle().DynamicVars.Cards.BaseValue);
        Assert.Equal(2m, new Whetstone().DynamicVars.Cards.BaseValue);
        Assert.True(new Whetstone().HasUponPickupEffect);
        var nunchaku = new Nunchaku();
        Assert.Equal(10m, nunchaku.DynamicVars.Cards.BaseValue);
        Assert.Equal(1m, nunchaku.DynamicVars.Energy.BaseValue);
        Assert.True(new PaperPhrog() is IVulnerableMultiplierModifier);
        Assert.True(new PaperKrane() is IWeakMultiplierModifier);
        var potions = new (string Id, PotionRarity Rarity, PotionUsage Usage, TargetType Target, bool InCombat)[]
        {
            (AttackPotion.PotionId, PotionRarity.Common, PotionUsage.CombatOnly, TargetType.AnyPlayer, true),
            (Ashwater.PotionId, PotionRarity.Uncommon, PotionUsage.CombatOnly, TargetType.AnyPlayer, true),
            (FairyInABottle.PotionId, PotionRarity.Rare, PotionUsage.Automatic, TargetType.Self, false),
            (StrengthPotion.PotionId, PotionRarity.Common, PotionUsage.CombatOnly, TargetType.AnyPlayer, true),
            (BlockPotion.PotionId, PotionRarity.Common, PotionUsage.CombatOnly, TargetType.AnyPlayer, true),
            (FirePotion.PotionId, PotionRarity.Common, PotionUsage.CombatOnly, TargetType.AnyEnemy, true)
        };
        foreach (var (id, rarity, usage, target, inCombat) in potions)
        {
            var potion = PotionRegistry.Create(id);
            Assert.Equal(id, potion.Id);
            Assert.Equal(rarity, potion.Rarity, id);
            Assert.Equal(usage, potion.Usage, id);
            Assert.Equal(target, potion.TargetType, id);
            Assert.Equal(inCombat, potion.CanBeGeneratedInCombat, id);
            Assert.True(potion.PassesCustomUsabilityCheck, id);
        }
        Assert.True(PotionRegistry.Count >= 4);
        Assert.Equal(2m, new StrengthPotion().DynamicVars.Strength.BaseValue);
        var block = new BlockPotion().DynamicVars.Block;
        Assert.Equal(12m, block.BaseValue);
        Assert.Equal(ValueProp.Unpowered, block.Props);
        var fire = new FirePotion().DynamicVars.Damage;
        Assert.Equal(20m, fire.BaseValue);
        Assert.Equal(ValueProp.Unpowered, fire.Props);
        Assert.Equal(0.3m, FairyInABottle.HealMaxHpFraction);
        Assert.Equal(3, AttackPotion.CardsOffered);
    }

    public static void Test_RelicPotion_HappyFlowerGainsEnergyEveryThirdTurnAcrossCombats()
    {
        var cs = NewCombat();
        var flower = Equip(cs, new HappyFlower());
        var players = new CreatureState[] { cs.Player };
        var enemies = new CreatureState[] { cs.Monsters[0] };
        cs.Player.Energy = 3;
        flower.AfterSideTurnStart(cs, CombatSide.Enemy, enemies);
        Assert.Equal(0, flower.TurnsSeen, "the owner is not a participant of the enemy turn");
        flower.AfterSideTurnStart(cs, CombatSide.Player, players);
        Assert.Equal(1, flower.TurnsSeen);
        Assert.Equal(RelicStatus.Normal, flower.Status);
        Assert.Equal(3, cs.Player.Energy);
        flower.AfterSideTurnStart(cs, CombatSide.Player, players);
        Assert.Equal(2, flower.TurnsSeen);
        Assert.Equal(RelicStatus.Active, flower.Status);
        Assert.Equal(2, flower.DisplayAmount);
        flower.AfterSideTurnStart(cs, CombatSide.Player, players);
        Assert.Equal(0, flower.TurnsSeen);
        Assert.Equal(RelicStatus.Normal, flower.Status);
        Assert.Equal(4, cs.Player.Energy, "every third turn gives 1 energy");
        flower.AfterSideTurnStart(cs, CombatSide.Player, players);
        flower.AfterSideTurnStart(cs, CombatSide.Player, players);
        Assert.Equal(RelicStatus.Active, flower.Status);
        flower.AfterCombatEnd(cs.Run, null);
        Assert.Equal(RelicStatus.Normal, flower.Status, "AfterCombatEnd resets the status");
        Assert.Equal(2, flower.TurnsSeen, "the counter persists across combats");
        var next = NewCombat();
        var carried = Equip(next, (HappyFlower)flower.CloneFor(null));
        next.Player.Energy = 3;
        carried.AfterSideTurnStart(next, CombatSide.Player, new CreatureState[] { next.Player });
        Assert.Equal(0, carried.TurnsSeen);
        Assert.Equal(4, next.Player.Energy, "the third turn may fall on the first turn of the next combat");
    }

    public static void Test_RelicPotion_CentennialPuzzleDrawsThreeOnTheFirstUnblockedHitEachCombat()
    {
        var cs = NewCombat();
        var puzzle = Equip(cs, new CentennialPuzzle());
        var def = new CardDouble("RELIC_POTION_SKILL", CardType.Skill);
        for (int i = 0; i < 6; i++)
        {
            AddCard(cs, cs.Player.DrawPile, def);
        }
        var monster = cs.Monsters[0];
        var blocked = new DamageResult(cs.Player, ValueProp.Move) { BlockedDamage = 5 };
        puzzle.AfterDamageReceived(cs.Run, cs.Player, blocked, ValueProp.Move, monster, null);
        Assert.Equal(0, cs.Player.Hand.Count, "a fully blocked hit");
        var monsterHit = new DamageResult(monster, ValueProp.Move) { UnblockedDamage = 4 };
        puzzle.AfterDamageReceived(cs.Run, monster, monsterHit, ValueProp.Move, cs.Player, null);
        Assert.Equal(0, cs.Player.Hand.Count, "a hit on another creature");
        var hit = new DamageResult(cs.Player, ValueProp.Move) { UnblockedDamage = 4 };
        puzzle.AfterDamageReceived(cs.Run, cs.Player, hit, ValueProp.Move, monster, null);
        Assert.Equal(3, cs.Player.Hand.Count);
        Assert.Equal(3, cs.Player.DrawPile.Count);
        Assert.True(puzzle.UsedThisCombat);
        puzzle.AfterDamageReceived(cs.Run, cs.Player, hit, ValueProp.Move, monster, null);
        Assert.Equal(3, cs.Player.Hand.Count, "once per combat");
        puzzle.AfterCombatEnd(cs.Run, null);
        Assert.False(puzzle.UsedThisCombat, "AfterCombatEnd resets the flag");
        cs.IsInProgress = false;
        puzzle.AfterDamageReceived(cs.Run, cs.Player, hit, ValueProp.Move, monster, null);
        Assert.False(puzzle.UsedThisCombat, "no draw outside a combat in progress");
        cs.IsInProgress = true;
        puzzle.AfterDamageReceived(cs.Run, cs.Player, hit, ValueProp.Move, monster, null);
        Assert.Equal(6, cs.Player.Hand.Count);
    }

    public static void Test_RelicPotion_NunchakuGainsEnergyEveryTenthAttack()
    {
        var cs = NewCombat();
        var nunchaku = Equip(cs, new Nunchaku());
        var attack = AddCard(cs, cs.Player.Hand, new CardDouble("RELIC_POTION_ATTACK", CardType.Attack));
        var skill = AddCard(cs, cs.Player.Hand, new CardDouble("RELIC_POTION_SKILL", CardType.Skill));
        var unowned = new CardInstance(new CardDouble("RELIC_POTION_ATTACK", CardType.Attack), cs.AllocateCardInstanceId());
        var monster = cs.Monsters[0];
        cs.Player.Energy = 0;
        nunchaku.AfterCardPlayed(cs, new CardPlay(cs, skill, null));
        nunchaku.AfterCardPlayed(cs, new CardPlay(cs, unowned, monster));
        Assert.Equal(0, nunchaku.AttacksPlayed, "only the owner's attacks count");
        for (int i = 1; i <= 9; i++)
        {
            nunchaku.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
            Assert.Equal(i, nunchaku.AttacksPlayed);
            Assert.Equal(i == 9 ? RelicStatus.Active : RelicStatus.Normal, nunchaku.Status, $"attack {i}");
        }
        Assert.Equal(0, cs.Player.Energy);
        nunchaku.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        Assert.Equal(1, cs.Player.Energy);
        Assert.Equal(RelicStatus.Normal, nunchaku.Status);
        Assert.Equal(0, nunchaku.DisplayAmount);
        cs.IsInProgress = false;
        for (int i = 0; i < 10; i++)
        {
            nunchaku.AfterCardPlayed(cs, new CardPlay(cs, attack, monster));
        }
        Assert.Equal(20, nunchaku.AttacksPlayed, "the counter still counts");
        Assert.Equal(1, cs.Player.Energy, "no energy outside a combat in progress");
    }

    public static void Test_RelicPotion_PaperPhrogAndPaperKraneGuards()
    {
        var cs = NewCombat();
        var phrog = Equip(cs, new PaperPhrog());
        var krane = Equip(cs, new PaperKrane());
        var monster = cs.Monsters[0];
        Assert.Equal(1.75m, phrog.ModifyVulnerableMultiplier(cs, monster, 1.5m, ValueProp.Move, cs.Player, null));
        Assert.Equal(1.5m, phrog.ModifyVulnerableMultiplier(cs, cs.Player, 1.5m, ValueProp.Move, monster, null), "the owner as target is unchanged");
        Assert.Equal(1.5m, phrog.ModifyVulnerableMultiplier(cs, monster, 1.5m, ValueProp.Move | ValueProp.Unpowered, cs.Player, null), "an unpowered attack");
        Assert.Equal(1.5m, phrog.ModifyVulnerableMultiplier(cs, monster, 1.5m, ValueProp.None, cs.Player, null), "not a move");
        Assert.Equal(0.6m, krane.ModifyWeakMultiplier(cs, cs.Player, 0.75m, ValueProp.Move, monster, null));
        Assert.Equal(0.75m, krane.ModifyWeakMultiplier(cs, monster, 0.75m, ValueProp.Move, cs.Player, null), "another creature as target is unchanged");
        Assert.Equal(0.75m, krane.ModifyWeakMultiplier(cs, cs.Player, 0.75m, ValueProp.Move | ValueProp.Unpowered, monster, null), "an unpowered attack");
        phrog.IsWax = true;
        RelicCmd.Melt(cs.Run, phrog);
        Assert.Equal(1.75m, phrog.ModifyVulnerableMultiplier(cs, monster, 1.5m, ValueProp.Move, cs.Player, null), "the relic's own method has no melted check (Models.Relics/PaperPhrog.cs:16-27)");
    }

    public static void Test_RelicPotion_PaperPhrogAndPaperKraneThroughVulnerableAndWeak()
    {
        W1Pending.Run(nameof(Test_RelicPotion_PaperPhrogAndPaperKraneThroughVulnerableAndWeak), Pipeline, () =>
        {
            var cs = NewCombat(monsters: 2);
            Equip(cs, new PaperPhrog());
            Equip(cs, new PaperKrane());
            var monster = cs.Monsters[0];
            var other = cs.Monsters[1];
            var vulnerable = PowerRegistry.Create("VULNERABLE_POWER");
            vulnerable.ApplyInternal(monster, 2);
            decimal fromPlayer = vulnerable.ModifyDamageMultiplicative(cs.Run, monster, 10m, ValueProp.Move, cs.Player, null, null);
            if (fromPlayer == 1.5m)
            {
                throw new NotImplementedException("pipeline: VulnerablePower does not consult the dealer's PaperPhrog yet (Models.Powers/VulnerablePower.cs:109-115, CONTRACTS Part B section B12 A5)");
            }
            Assert.Equal(1.75m, fromPlayer);
            Assert.Equal(1.5m, vulnerable.ModifyDamageMultiplicative(cs.Run, monster, 10m, ValueProp.Move, other, null, null), "a monster dealer has no player and no relic");
            var pet = new MonsterState(20, new MonsterDouble(), 10, 10, null, null) { Side = CombatSide.Player };
            Assert.Equal(1.5m, vulnerable.ModifyDamageMultiplicative(cs.Run, monster, 10m, ValueProp.Move, pet, null, null), "a player-side pet has no player either (VulnerablePower.cs:111)");
            var weak = PowerRegistry.Create("WEAK_POWER");
            weak.ApplyInternal(monster, 2);
            decimal onPlayer = weak.ModifyDamageMultiplicative(cs.Run, cs.Player, 10m, ValueProp.Move, monster, null, null);
            if (onPlayer == 0.75m)
            {
                throw new NotImplementedException("pipeline: WeakPower does not consult the target's PaperKrane yet (Models.Powers/WeakPower.cs:109-113, CONTRACTS Part B section B12 A5)");
            }
            Assert.Equal(0.6m, onPlayer);
            Assert.Equal(0.75m, weak.ModifyDamageMultiplicative(cs.Run, other, 10m, ValueProp.Move, monster, null, null), "a monster target has no relic");
        });
    }

    public static void Test_RelicPotion_GamblingChipOnlyActsForItsOwnerOnTurnOne()
    {
        var cs = NewCombat();
        var chip = Equip(cs, new GamblingChip());
        var other = new PlayerState(9, StartingDecks.IroncladCharacterId, 10, 10, 3);
        ulong hash = Hash(cs);
        chip.AfterPlayerTurnStart(cs, other);
        cs.Player.TurnNumber = 2;
        chip.AfterPlayerTurnStart(cs, cs.Player);
        cs.Player.TurnNumber = 1;
        Assert.NotEqual(0UL, hash);
        Assert.Equal(Hash(cs), hash, "no prompt for another player or after turn 1");
    }

    public static void Test_RelicPotion_GamblingChipDiscardsThePickedCardsAndDrawsAsMany()
    {
        W1Pending.Run(nameof(Test_RelicPotion_GamblingChipDiscardsThePickedCardsAndDrawsAsMany), Choice + "+" + Loop, () =>
        {
            var cs = NewCombat();
            var chip = Equip(cs, new GamblingChip());
            var def = new CardDouble("RELIC_POTION_SKILL", CardType.Skill);
            var hand = new List<CardInstance>();
            for (int i = 0; i < 5; i++)
            {
                hand.Add(AddCard(cs, cs.Player.Hand, def));
                AddCard(cs, cs.Player.DrawPile, def);
            }
            var answerer = new RecordingAnswerer(0, 2);
            cs.Choice.Answerer = answerer;
            chip.AfterPlayerTurnStart(cs, cs.Player);
            var prompt = answerer.Prompts.Single();
            Assert.Equal(ChoicePromptKind.HandSelect, prompt.Kind);
            Assert.Equal("GAMBLING_CHIP:AfterPlayerTurnStart", prompt.SiteId);
            Assert.Equal(GamblingChip.RelicId, prompt.SourceId);
            Assert.Equal(0, prompt.MinSelect);
            Assert.Equal(5, prompt.MaxSelect, "clipped to the option count");
            Assert.True(prompt.RequireConfirm);
            Assert.SequenceEqual(hand.Select(c => c.InstanceId), prompt.Options.Select(o => o.InstanceId));
            Assert.SequenceEqual(new[] { hand[0], hand[2] }, cs.Player.DiscardPile.Cards);
            Assert.Equal(5, cs.Player.Hand.Count, "as many cards drawn as discarded");
            Assert.Equal(3, cs.Player.DrawPile.Count);
        });
    }

    public static void Test_RelicPotion_WhetstoneUpgradesTwoNicheShuffledDeckAttacks()
    {
        W1Pending.Run(nameof(Test_RelicPotion_WhetstoneUpgradesTwoNicheShuffledDeckAttacks), CardsKernel, () =>
        {
            var cs = NewCombat();
            var attackA = new CardDouble("RELIC_POTION_ATTACK_A", CardType.Attack);
            var attackB = new CardDouble("RELIC_POTION_ATTACK_B", CardType.Attack);
            var fixedAttack = new CardDouble("RELIC_POTION_ATTACK_FIXED", CardType.Attack, maxUpgradeLevel: 0);
            var skill = new CardDouble("RELIC_POTION_SKILL", CardType.Skill);
            var deck = cs.Player.Deck;
            foreach (var def in new CardDef[] { attackB, skill, attackA, fixedAttack, attackA, attackB, skill })
            {
                var card = new CardInstance(def, cs.AllocateCardInstanceId());
                card.AttachTo(cs.Player);
                deck.Add(card);
            }
            var mirror = cs.Rng.Niche.DeepClone();
            var candidates = deck.Where(c => c.Type == CardType.Attack && c.IsUpgradable).ToList();
            candidates.StableShuffle(mirror, CardPileCmd.CompareForShuffle);
            var expected = candidates.Take(2).ToList();
            RelicCmd.Obtain(cs.Run, new Whetstone(), cs.Player);
            Assert.Equal(mirror.Counter, cs.Rng.Niche.Counter, "one Niche draw per shuffled candidate but the first");
            foreach (var card in deck)
            {
                Assert.Equal(expected.Contains(card) ? 1 : 0, card.UpgradeLevel, $"{card.Id}#{card.InstanceId}");
            }
        });
    }

    public static void Test_RelicPotion_StrengthBlockAndFirePotionNumbers()
    {
        var cs = NewCombat();
        var monster = cs.Monsters[0];
        var strength = Slot(cs, new StrengthPotion(), 0);
        InvokeOnUse(strength, cs.Run, cs.Player);
        var power = Assert.NotNull(cs.Player.GetPower("STRENGTH_POWER"));
        Assert.Equal(2, power.Amount);
        Assert.Equal(cs.Player.CombatId, power.ApplierId);
        InvokeOnUse(strength, cs.Run, cs.Player);
        Assert.Equal(4, power.Amount, "a second dose stacks");
        var block = Slot(cs, new BlockPotion(), 1);
        InvokeOnUse(block, cs.Run, cs.Player);
        Assert.Equal(12, cs.Player.Block);
        var fire = Slot(cs, new FirePotion(), 2);
        InvokeOnUse(fire, cs.Run, monster);
        Assert.Equal(30, monster.CurrentHp, "20 unpowered damage ignores the owner's 4 Strength");
        Assert.Throws<ArgumentNullException>(() => InvokeOnUse(fire, cs.Run, null), "AssertValidForTargetedPotion");
        var outOfCombat = RunState.CreateStandalone(new PlayerState(3, StartingDecks.IroncladCharacterId, 10, 10, 3), new RunRngSet(1UL), 0);
        Assert.Throws<NotImplementedException>(() => InvokeOnUse(new StrengthPotion(), outOfCombat, outOfCombat.Players[0]), "out-of-combat potion effects are W2");
    }

    public static void Test_RelicPotion_FairyInABottleHealsThirtyPercentAndOnlyGuardsItsOwner()
    {
        var cs = NewCombat();
        var fairy = Slot(cs, new FairyInABottle(), 0);
        Assert.False(fairy.ShouldDie(cs.Run, cs.Player));
        Assert.True(fairy.ShouldDie(cs.Run, cs.Monsters[0]));
        Assert.True(fairy.ShouldDieLate(cs.Run, cs.Player));
        var cases = new (int MaxHp, int Healed)[] { (80, 24), (25, 7), (3, 1), (1, 1) };
        foreach (var (maxHp, healed) in cases)
        {
            var combat = NewCombat(playerHp: maxHp);
            var potion = Slot(combat, new FairyInABottle(), 0);
            combat.Player.CurrentHp = 0;
            InvokeOnUse(potion, combat.Run, combat.Player);
            Assert.Equal(healed, combat.Player.CurrentHp, $"max HP {maxHp}");
        }
    }

    public static void Test_RelicPotion_FairyInABottlePreventsTheOwnersDeathThroughCreatureCmd()
    {
        W1Pending.Run(nameof(Test_RelicPotion_FairyInABottlePreventsTheOwnersDeathThroughCreatureCmd), Hooks, () =>
        {
            var cs = NewCombat();
            cs.Player.CurrentHp = 10;
            var fairy = Slot(cs, new FairyInABottle(), 1);
            RequireListeners(cs, fairy);
            CreatureCmd.Damage(cs, cs.Player, 15m, ValueProp.Unpowered, cs.Monsters[0], null, null);
            Assert.Equal(24, cs.Player.CurrentHp, "healed by max(80 x 0.3, 1) from 0");
            Assert.True(cs.Player.IsActiveForHooks);
            Assert.Null(cs.Player.PotionSlots[1]);
            Assert.True(fairy.HasBeenRemovedFromState);
            Assert.Equal(CombatOutcome.InProgress, cs.Outcome);
            var entry = cs.History.PotionUsedEntries.Single();
            Assert.Equal(FairyInABottle.PotionId, entry.PotionId);
            Assert.Equal(cs.Player.CombatId, entry.TargetId);
            Assert.Equal(0, cs.Choice.ContextDepth);
            CreatureCmd.Damage(cs, cs.Player, 30m, ValueProp.Unpowered, cs.Monsters[0], null, null);
            Assert.True(cs.Player.IsDead, "the bottle is spent");
            Assert.Equal(CombatOutcome.Defeat, cs.Outcome);

            var guarded = NewCombat();
            Slot(guarded, new FairyInABottle(), 0);
            var guardedMonster = guarded.Monsters[0];
            CreatureCmd.Damage(guarded, guardedMonster, 60m, ValueProp.Unpowered, guarded.Player, null, null);
            Assert.True(guardedMonster.IsDead, "the bottle saves only its owner");
            Assert.True(guarded.Player.PotionSlots[0] is FairyInABottle);
        });
    }

    public static void Test_RelicPotion_UsePotionDrinksTheSlotAndRecordsHistory()
    {
        W1Pending.Run(nameof(Test_RelicPotion_UsePotionDrinksTheSlotAndRecordsHistory), Hooks, () =>
        {
            var cs = NewCombat();
            var monster = cs.Monsters[0];
            Slot(cs, new StrengthPotion(), 1);
            Slot(cs, new FirePotion(), 0);
            PotionActions.UsePotion(cs, 1, null);
            Assert.Equal(2, cs.Player.GetPowerAmount<StrengthPower>());
            Assert.Null(cs.Player.PotionSlots[1]);
            PotionActions.UsePotion(cs, 0, monster);
            Assert.Equal(30, monster.CurrentHp);
            Assert.Null(cs.Player.PotionSlots[0]);
            Assert.SequenceEqual(new[] { (StrengthPotion.PotionId, cs.Player.CombatId), (FirePotion.PotionId, monster.CombatId) }, cs.History.PotionUsedEntries.Select(e => (e.PotionId, e.TargetId)));
            Assert.Equal(0, cs.CardOrPotionEffectDepth);
            Assert.Equal(0, cs.Choice.ContextDepth);
            Assert.Equal(Hash(cs), Hash(cs.DeepClone()));
        });
    }

    public static void Test_RelicPotion_AttackPotionOffersThreeAttacksAndMakesThePickFree()
    {
        W1Pending.Run(nameof(Test_RelicPotion_AttackPotionOffersThreeAttacksAndMakesThePickFree), CardsKernel + "+" + Choice, () =>
        {
            var cs = NewCombat();
            Slot(cs, new AttackPotion(), 0);
            var attackOptions = CardFactory.FilterForCombat(CardPools.ForCharacter(cs.Player.CharacterId).GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run)).Where(c => c.Type == CardType.Attack)).ToList();
            int ported = attackOptions.Count(c => c is not UnportedCardDef);
            int offered = Math.Min(3, ported);
            int pick = Math.Min(1, offered - 1);
            var answerer = new RecordingAnswerer(pick);
            cs.Choice.Answerer = answerer;
            int generationBefore = cs.Rng.CombatCardGeneration.Counter;
            PotionActions.UsePotion(cs, 0, null);
            var prompt = answerer.Prompts.Single();
            Assert.Equal(ChoicePromptKind.ChooseCard, prompt.Kind);
            Assert.Equal("ATTACK_POTION:OnUse", prompt.SiteId);
            Assert.Equal(offered, prompt.Options.Count, "three distinct attacks; while fewer than three are ported the draw runs over the ported ones (cards-kernel.md section 7.4)");
            if (ported < attackOptions.Count)
            {
                Assert.True(cs.Run.Fidelity.Contains(FidelityFlag.ApproximatedRule), "the draw over the ported options is flagged (cards-kernel.md section 7.4)");
            }
            Assert.True(prompt.CanSkip);
            if (ported > 1)
            {
                Assert.True(cs.Rng.CombatCardGeneration.Counter > generationBefore, "the offer draws on CombatCardGeneration (a one-option shuffle draws nothing, Random/RngCollectionExtensions.cs UnstableShuffle)");
            }
            var picked = cs.Player.Hand.Cards.Single();
            Assert.Equal(prompt.Options[pick].InstanceId, picked.InstanceId);
            Assert.Equal(CardType.Attack, picked.Type);
            Assert.Equal(0, picked.EnergyCost.GetResolved(cs, picked), "free this turn");
            Assert.Null(cs.Player.PotionSlots[0]);
        });
    }

    public static void Test_RelicPotion_AshwaterExhaustsTheChosenHandCards()
    {
        W1Pending.Run(nameof(Test_RelicPotion_AshwaterExhaustsTheChosenHandCards), Choice, () =>
        {
            var cs = NewCombat();
            var def = new CardDouble("RELIC_POTION_SKILL", CardType.Skill);
            var hand = new List<CardInstance>();
            for (int i = 0; i < 4; i++)
            {
                hand.Add(AddCard(cs, cs.Player.Hand, def));
            }
            var ashwater = Slot(cs, new Ashwater(), 0);
            var answerer = new RecordingAnswerer(3, 1);
            cs.Choice.Answerer = answerer;
            InvokeOnUse(ashwater, cs.Run, cs.Player);
            var prompt = answerer.Prompts.Single();
            Assert.Equal(ChoicePromptKind.HandSelect, prompt.Kind);
            Assert.Equal("ASHWATER:OnUse", prompt.SiteId);
            Assert.Equal(0, prompt.MinSelect);
            Assert.Equal(4, prompt.MaxSelect, "clipped to the option count");
            Assert.True(prompt.RequireConfirm);
            Assert.SequenceEqual(new[] { hand[3], hand[1] }, cs.Player.ExhaustPile.Cards, "exhausted in pick order");
            Assert.SequenceEqual(new[] { hand[0], hand[2] }, cs.Player.Hand.Cards);
        });
    }
}
