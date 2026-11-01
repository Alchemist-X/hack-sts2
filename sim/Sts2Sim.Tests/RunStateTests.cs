using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Tests;

public static class RunStateTests
{
    private sealed class EncounterDouble : EncounterDef
    {
        private readonly RoomType _roomType;
        private readonly bool _isWeak;
        private readonly string[] _tags;

        public EncounterDouble(string id, RoomType roomType, bool isWeak = false, params string[] tags)
            : base(id)
        {
            _roomType = roomType;
            _isWeak = isWeak;
            _tags = tags;
        }

        public override RoomType RoomType => _roomType;

        public override bool IsWeak => _isWeak;

        public override IReadOnlyList<string> Tags => _tags;

        public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw new NotSupportedException();
    }

    private sealed class ActDouble : ActModel
    {
        private readonly string _id;
        private readonly int _index;
        private readonly bool _isDefault;
        private readonly int _rooms;
        private readonly EncounterDef[] _encounters;

        public ActDouble(string id, int index, bool isDefault, int rooms = 15, EncounterDef[]? encounters = null)
        {
            _id = id;
            _index = index;
            _isDefault = isDefault;
            _rooms = rooms;
            _encounters = encounters ?? Array.Empty<EncounterDef>();
        }

        public override string Id => _id;

        public override int Index => _index;

        public override bool IsDefault => _isDefault;

        protected override int BaseNumberOfRooms => _rooms;

        public override IEnumerable<EncounterDef> BossDiscoveryOrder => _encounters.Where(e => e.RoomType == RoomType.Boss);

        public override IEnumerable<string> AllAncientIds => new[] { "TEST_ANCIENT" };

        public override IEnumerable<string> AllEventIds => new[] { "TEST_EVENT_A", "TEST_EVENT_B" };

        public override IEnumerable<EncounterDef> GenerateAllEncounters() => _encounters;

        public override bool IsUnlocked(UnlockProfile unlockState) => true;

        public override IEnumerable<string> GetUnlockedAncientIds(UnlockProfile state) => AllAncientIds;

        public override MapPointTypeCounts GetMapPointTypes(Rng mapRng) => new(0, 0);
    }

    private static EncounterDef[] SampleEncounters() => new EncounterDef[]
    {
        new EncounterDouble("W1", RoomType.Monster, true, "a"),
        new EncounterDouble("W2", RoomType.Monster, true, "b"),
        new EncounterDouble("N1", RoomType.Monster, false, "c"),
        new EncounterDouble("N2", RoomType.Monster, false, "d"),
        new EncounterDouble("N3", RoomType.Monster, false),
        new EncounterDouble("E1", RoomType.Elite),
        new EncounterDouble("E2", RoomType.Elite),
        new EncounterDouble("B1", RoomType.Boss),
        new EncounterDouble("B2", RoomType.Boss),
    };

    private static RunState NewRun(int ascension = 0)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        return RunState.CreateStandalone(player, new RunRngSet("RUNSTATE"), ascension);
    }

    private static RunState PopulatedRun()
    {
        var rs = NewRun();
        var encounters = SampleEncounters();
        var act = new ActInstance(new ActDouble("ACT_A", 0, true, 15, encounters));
        act.Rooms.events.AddRange(new[] { "EV1", "EV2" });
        act.Rooms.normalEncounters.AddRange(encounters.Take(3));
        act.Rooms.eliteEncounters.Add(encounters[5]);
        act.Rooms.Boss = encounters[7];
        act.Rooms.AncientId = "TEST_ANCIENT";
        act.SetSharedAncientSubset(new List<string> { ActModel.DarvId });
        rs.Acts.Add(act);
        rs.AddVisitedMapCoord(new MapCoord(1, 0));
        rs.AppendToMapPointHistory(MapPointType.Ancient, RoomType.Event, "TEST_ANCIENT");
        rs.VisitedEventIds.Add("EV1");
        rs.ExtraFields.TestSubjectKills = 2;
        rs.Players[0].ExtraFields.CardShopRemovalsUsed = 1;
        return rs;
    }

    public static void Test_RunState_CloneHashInvariantOverRunLayer()
    {
        var rs = PopulatedRun();
        var clone = rs.DeepClone();
        Assert.Equal(StateHasher.Hash(rs), StateHasher.Hash(clone));
        clone.Act.Rooms.MarkVisited(RoomType.Monster);
        clone.MapPointHistory[0].GoldGained = 7;
        Assert.NotEqual(StateHasher.Hash(rs), StateHasher.Hash(clone));
        Assert.Equal(0, rs.Act.Rooms.normalEncountersVisited);
        Assert.Equal(0, rs.MapPointHistory[0].GoldGained);
        Assert.False(ReferenceEquals(rs.Act, clone.Act));
        Assert.True(ReferenceEquals(rs.Act.Def, clone.Act.Def));
    }

    public static void Test_RunState_CanonicalStableAcrossIdenticalBuilds()
    {
        var a = PopulatedRun();
        var b = PopulatedRun();
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
        b.ExtraFields.StartedWithNeow = true;
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
        var c = PopulatedRun();
        c.Act.Rooms.SecondBoss = c.Act.Def.AllBossEncounters[1];
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(c));
    }

    public static void Test_RunState_CurrentActIndexSetterResetsActScope()
    {
        var rs = PopulatedRun();
        rs.ActFloor = 5;
        rs.NextRoomId = 3;
        rs.CurrentActIndex = 0;
        Assert.Equal(1, rs.VisitedMapCoords.Count);
        Assert.Equal(5, rs.ActFloor);
        rs.CurrentActIndex = 1;
        Assert.Equal(0, rs.VisitedMapCoords.Count);
        Assert.Equal(0, rs.ActFloor);
        Assert.Equal(0, rs.NextRoomId);
        Assert.Equal(1, rs.CurrentActIndex);
        rs.NextRoomId = 4;
        Assert.True(rs.AddVisitedMapCoord(new MapCoord(0, 1)));
        Assert.Equal(0, rs.NextRoomId);
        Assert.False(rs.AddVisitedMapCoord(new MapCoord(0, 1)));
    }

    public static void Test_RunState_HistoryFloorsAndLookup()
    {
        var rs = PopulatedRun();
        rs.AppendToMapPointHistory(MapPointType.Monster, RoomType.Monster, "W1");
        rs.AppendToMapPointHistory(MapPointType.Shop, RoomType.Shop, null);
        Assert.Equal(3, rs.TotalFloor);
        Assert.True(rs.CurrentMapPointHistoryEntry!.HasRoomOfType(RoomType.Shop));
        Assert.False(rs.CurrentMapPointHistoryEntry!.HasRoomOfType(RoomType.Monster));
        Assert.Equal(MapPointType.Monster, rs.GetHistoryEntryFor(0, new MapCoord(0, 1))!.MapPointType);
        Assert.Null(rs.GetHistoryEntryFor(1, new MapCoord(0, 0)));
    }

    public static void Test_RoomSet_ModuloPullsAndSecondBoss()
    {
        var rs = PopulatedRun();
        var act = rs.Act;
        Assert.Equal("W1", act.PullNextEncounter(RoomType.Monster).Id);
        act.MarkRoomVisited(RoomType.Monster);
        act.MarkRoomVisited(RoomType.Monster);
        act.MarkRoomVisited(RoomType.Monster);
        Assert.Equal("W1", act.PullNextEncounter(RoomType.Monster).Id);
        Assert.Equal("E1", act.PullNextEncounter(RoomType.Elite).Id);
        Assert.Equal("B1", act.PullNextEncounter(RoomType.Boss).Id);
        act.SetSecondBossEncounter(act.Def.AllBossEncounters[1]);
        Assert.Equal("B1", act.PullNextEncounter(RoomType.Boss).Id);
        act.MarkRoomVisited(RoomType.Boss);
        Assert.Equal("B2", act.PullNextEncounter(RoomType.Boss).Id);
        Assert.Throws<ArgumentException>(() => act.SetBossEncounter(act.Def.AllEliteEncounters[0]));
        Assert.Equal("EV1", act.Rooms.NextEventId);
        act.Rooms.EnsureNextEventIsValid(rs);
        Assert.Equal("EV2", act.Rooms.NextEventId);
    }

    public static void Test_ActModel_RoomCountsFloorsAndFilters()
    {
        var act = new ActDouble("ACT_A", 0, true, 15, SampleEncounters());
        Assert.Equal(15, act.GetNumberOfRooms(false));
        Assert.Equal(14, act.GetNumberOfRooms(true));
        Assert.Equal(17, act.GetNumberOfFloors(false));
        Assert.SequenceEqual(new[] { "W1", "W2" }, act.AllWeakEncounters.Select(e => e.Id));
        Assert.SequenceEqual(new[] { "N1", "N2", "N3" }, act.AllRegularEncounters.Select(e => e.Id));
        Assert.SequenceEqual(new[] { "E1", "E2" }, act.AllEliteEncounters.Select(e => e.Id));
        Assert.SequenceEqual(new[] { "B1", "B2" }, act.AllBossEncounters.Select(e => e.Id));
        Assert.True(act.AllEncounters[0].SharesTagsWith(new EncounterDouble("X", RoomType.Monster, false, "z", "a")));
        Assert.False(act.AllEncounters[0].SharesTagsWith(act.AllEncounters[1]));
        Assert.False(act.AllEncounters[0].SharesTagsWith(null));
    }

    public static void Test_ActModel_RandomListForcesUndiscoveredNonDefault()
    {
        var a0 = new ActDouble("A0", 0, true);
        var a0b = new ActDouble("A0B", 0, false);
        var a1 = new ActDouble("A1", 1, true);
        var byIndex = ActModel.BuildActsByIndex(new ActModel[] { a0, a0b, a1 });
        Assert.Equal(2, byIndex.Count);
        var fresh = new RunProfileProgress(new[] { "A0" }, 1, 0);
        var forced = ActModel.GetRandomList(byIndex, new Rng(1UL), UnlockProfile.All, fresh, false).ToList();
        Assert.SequenceEqual(new[] { "A0B", "A1" }, forced.Select(a => a.Id));
        var rng = new Rng(1UL);
        var reference = new Rng(1UL);
        var veteran = ActModel.GetRandomList(byIndex, rng, UnlockProfile.All, RunProfileProgress.Veteran, false).ToList();
        int pick0 = reference.NextInt(0, 2);
        reference.NextInt(0, 1);
        Assert.Equal(pick0 == 0 ? "A0" : "A0B", veteran[0].Id);
        Assert.Equal(reference.Counter, rng.Counter);
        Assert.SequenceEqual(new[] { "A0", "A1" }, ActModel.GetDefaultList(byIndex).Select(a => a.Id));
    }

    public static void Test_Ascension_RunStartEffects()
    {
        var a3 = NewRunWithoutEffects(3);
        a3.ApplyAscensionEffects(a3.Players[0]);
        Assert.Equal(3, a3.Players[0].PotionSlots.Length);
        Assert.Equal(0, a3.Players[0].Deck.Count);

        var a5 = NewRunWithoutEffects(5);
        var player = a5.Players[0];
        a5.ApplyAscensionEffects(player);
        Assert.Equal(2, player.PotionSlots.Length);
        Assert.Equal(1, player.Deck.Count);
        Assert.Equal(AscendersBane.CardId, player.Deck[0].Id);
        a5.ApplyAscensionEffects(player);
        Assert.Equal(1, player.Deck.Count);
        Assert.True(a5.HasAscension(AscensionLevel.TightBelt));
        Assert.False(a5.HasAscension(AscensionLevel.Inflation));
    }

    private static RunState NewRunWithoutEffects(int ascension)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3) { AscensionEffectsApplied = true };
        var rs = RunState.CreateStandalone(player, new RunRngSet("ASC"), ascension);
        player.AscensionEffectsApplied = false;
        return rs;
    }

    public static void Test_RunState_StartedWithNeowFromUnlocks()
    {
        var rs = NewRun();
        rs.SetStartedWithNeowFlag();
        Assert.True(rs.ExtraFields.StartedWithNeow);
        var none = RunState.CreateStandalone(new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3), new RunRngSet("N"), 0, UnlockProfile.None);
        none.SetStartedWithNeowFlag();
        Assert.False(none.ExtraFields.StartedWithNeow);
    }

    public static void Test_ActInstance_GenerateRoomsCountsAndUpFrontOrder()
    {
        W1Pending.Run(nameof(Test_ActInstance_GenerateRoomsCountsAndUpFrontOrder), "R3 odds-rewards (GrabBag)", () =>
        {
            var rs = NewRun(10);
            rs.Acts.Add(new ActInstance(new ActDouble("ACT_A", 0, true, 15, SampleEncounters())));
            rs.Acts.Add(new ActInstance(new ActDouble("ACT_B", 1, true, 14, SampleEncounters())));
            rs.GenerateRooms();
            var first = rs.Acts[0].Rooms;
            Assert.Equal(15, first.normalEncounters.Count);
            Assert.True(first.normalEncounters.Take(3).All(e => e.IsWeak));
            Assert.Equal(ActModel.EliteEncounterCount, first.eliteEncounters.Count);
            Assert.Equal(2 + ActModel.AllSharedEventIds.Count, first.events.Count);
            Assert.NotNull(first.Boss);
            Assert.Null(first.SecondBoss);
            Assert.NotNull(rs.Acts[1].Rooms.SecondBoss);
            Assert.NotEqual(rs.Acts[1].Rooms.Boss!.Id, rs.Acts[1].Rooms.SecondBoss!.Id);
            var again = NewRun(10);
            again.Acts.Add(new ActInstance(new ActDouble("ACT_A", 0, true, 15, SampleEncounters())));
            again.Acts.Add(new ActInstance(new ActDouble("ACT_B", 1, true, 14, SampleEncounters())));
            again.GenerateRooms();
            Assert.Equal(StateHasher.Hash(rs), StateHasher.Hash(again));
            Assert.Equal(StateHasher.Hash(rs), StateHasher.Hash(rs.DeepClone()));
        });
    }

    public static void Test_RunState_CreateForNewRunSeedsPlayer()
    {
        W1Pending.Run(nameof(Test_RunState_CreateForNewRunSeedsPlayer), "R3 odds-rewards (RunOddsSet, PlayerOddsSet)", () =>
        {
            var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
            var acts = new ActModel[] { new ActDouble("ACT_A", 0, true), new ActDouble("ACT_B", 1, true) };
            var rs = RunState.CreateForNewRun(player, acts, 0, "SEED");
            Assert.Equal(2, rs.Acts.Count);
            Assert.NotNull(rs.Odds);
            Assert.NotNull(rs.SharedRelicGrabBag);
            Assert.Equal(PlayerRngSet.ForPlayer("SEED", 0).Seed, player.PlayerRng!.Seed);
            Assert.NotNull(player.PlayerOdds);
        });
    }
}
