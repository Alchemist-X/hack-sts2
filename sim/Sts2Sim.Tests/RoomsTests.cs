using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class RoomsTests
{
    private sealed class PicksAnswerer : IChoiceAnswerer
    {
        private readonly int[] _picks;

        public PicksAnswerer(params int[] picks)
        {
            _picks = picks;
        }

        public ChoicePrompt? Last { get; private set; }

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            Last = prompt;
            return new ChoiceAnswer(_picks, false);
        }
    }

    private sealed class CancelAnswerer : IChoiceAnswerer
    {
        public ChoiceAnswer Answer(ChoicePrompt prompt) => new(Array.Empty<int>(), true);
    }

    private static RunState NewRun(int ascension = 0)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 50, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(4242UL), ascension);
        player.PlayerRng = new PlayerRngSet(99UL);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, ascension);
        player.RelicGrabBag = new RelicGrabBag(false);
        player.RelicGrabBag.Populate(run, player, run.Rng.UpFront);
        run.SharedRelicGrabBag = new RelicGrabBag(true);
        run.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(run), run.Rng.UpFront);
        run.Odds = new RunOddsSet(run.Rng.UnknownMapPoint);
        return run;
    }

    private static CardInstance AddDeck(RunState rs, string id)
    {
        var player = rs.Players[0];
        var card = new CardInstance(CardRegistry.Get(id), rs.AllocateCardInstanceId());
        card.AttachTo(player);
        player.Deck.Add(card);
        return card;
    }

    private static ulong Hash(ICanonical value)
    {
        var w = new CanonicalWriter();
        value.WriteCanonical(w);
        return StateHasher.Hash(w.WrittenSpan);
    }

    public static void Test_Rooms_EncounterRoomTypesCoverRegisteredEncounters()
    {
        Assert.Equal(90, EncounterRoomTypes.Count);
        Assert.Equal(RoomType.Monster, EncounterRoomTypes.Of("SLIMES_WEAK"));
        Assert.Equal(RoomType.Elite, EncounterRoomTypes.Of("BYRDONIS_ELITE"));
        Assert.Equal(RoomType.Boss, EncounterRoomTypes.Of("THE_KIN_BOSS"));
        Assert.False(EncounterRoomTypes.ShouldGiveRewards("BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER"));
        Assert.True(EncounterRoomTypes.ShouldGiveRewards("SLIMES_WEAK"));
        foreach (var id in EncounterRegistry.AllIds)
        {
            Assert.True(EncounterRoomTypes.TryGet(id, out _), $"registered encounter {id} has a room type");
        }
    }

    public static void Test_Rooms_EnterAssignsRoomIdsAndMapRoomSkipsBeforeRoomEntered()
    {
        var rs = NewRun();
        rs.NextRoomId = 3;
        rs.CurrentRooms.Add(new MapRoom());
        var room = new MapRoom();
        RoomStack.EnterRoomInternal(rs, room, false);
        Assert.Throws<InvalidOperationException>(() => RoomStack.EnterRoomInternal(rs, room, false), "a room already on the stack");
        Assert.Equal(3, room.Id);
        Assert.Equal(4, rs.NextRoomId);
        Assert.True(ReferenceEquals(room, RoomStack.CurrentRoom(rs)));
        RoomStack.ExitCurrentRooms(rs);
        Assert.Equal(0, rs.CurrentRooms.Count);
    }

    public static void Test_Rooms_CombatRoomStartsCombatOnTheRunAndExitDetachesIt()
    {
        var rs = NewRun();
        var room = new CombatRoom(EncounterRegistry.Get("SLIMES_WEAK"));
        Assert.Equal(RoomType.Monster, room.RoomType);
        Assert.True(room.ShouldResumeParentEventAfterCombat, "the game's default is true");
        rs.CurrentRooms.Add(room);
        var cs = room.StartCombat(rs);
        Assert.True(ReferenceEquals(cs, rs.Combat), "the combat is attached to the run");
        Assert.True(cs.Monsters.Count > 0, "monsters were spawned");
        room.AfterCombatVictory(rs, cs);
        Assert.True(room.IsPreFinished);
        Assert.Equal(1m, room.GoldProportion);
        var clone = (CombatRoom)room.CloneFor(rs);
        Assert.Equal(Hash(room), Hash(clone), "clone hashes equal");
        room.Exit(rs);
        Assert.Null(rs.Combat);
    }

    public static void Test_Rooms_MerchantRemovalCostAndCurseFirstPrompt()
    {
        var rs = NewRun();
        var player = rs.Players[0];
        var entry = new MerchantCardRemovalEntry(rs, player);
        Assert.Equal(75, entry.Cost);
        player.ExtraFields.CardShopRemovalsUsed = 2;
        entry.CalcCost();
        Assert.Equal(125, entry.Cost);
        var a6 = new MerchantCardRemovalEntry(NewRun((int)AscensionLevel.Inflation), player);
        Assert.Equal(200, a6.Cost);

        var strike = AddDeck(rs, "STRIKE_IRONCLAD");
        AddDeck(rs, "ASCENDERS_BANE");
        var doubt = AddDeck(rs, "DOUBT");
        player.Gold = 300;
        player.ExtraFields.CardShopRemovalsUsed = 0;
        entry.CalcCost();
        var answerer = new PicksAnswerer(0);
        rs.Choice.Answerer = answerer;
        Assert.True(entry.OnTryPurchaseWrapper(rs, null!, false, true));
        var prompt = answerer.Last!;
        Assert.Equal(2, prompt.Options.Count, "the eternal curse is not removable");
        Assert.Equal(doubt.InstanceId, prompt.Options[0].InstanceId, "curses sort first");
        Assert.Equal(strike.InstanceId, prompt.Options[1].InstanceId);
        Assert.True(prompt.Cancelable && prompt.RequireConfirm);
        Assert.False(player.Deck.Contains(doubt));
        Assert.Equal(225, player.Gold);
        Assert.Equal(1, player.ExtraFields.CardShopRemovalsUsed);
        Assert.True(entry.Used);
        Assert.False(entry.OnTryPurchaseWrapper(rs, null!, false, true), "used for this visit");

        var fresh = new MerchantCardRemovalEntry(rs, player);
        rs.Choice.Answerer = new CancelAnswerer();
        Assert.False(fresh.OnTryPurchaseWrapper(rs, null!, false, true), "cancel is no purchase");
        Assert.Equal(225, player.Gold);
        Assert.False(fresh.Used);
    }

    public static void Test_Rooms_RestSiteSmithClearsOptionsAndAllowsProceed()
    {
        var rs = NewRun();
        var strike = AddDeck(rs, "STRIKE_IRONCLAD");
        var room = new RestSiteRoom();
        rs.CurrentRooms.Add(room);
        room.Enter(rs);
        Assert.SequenceEqual(new[] { "HEAL", "SMITH" }, room.Options.Select(o => o.OptionId));
        Assert.False(room.ProceedAllowed);
        rs.Choice.Answerer = new CancelAnswerer();
        Assert.False(room.ChooseOption(rs, 1), "a cancelled smith changes nothing");
        Assert.Equal(2, room.Options.Count);
        Assert.False(room.ProceedAllowed);
        rs.Choice.Answerer = new PicksAnswerer(0);
        Assert.True(room.ChooseOption(rs, 1));
        Assert.Equal(1, strike.UpgradeLevel);
        Assert.Equal(0, room.Options.Count);
        Assert.True(room.ProceedAllowed);
        var clone = (RestSiteRoom)room.CloneFor(rs.DeepClone());
        Assert.Equal(Hash(room), Hash(clone));
    }

    public static void Test_Rooms_RestSiteHealIsThirtyPercentAndCookRemovesTwo()
    {
        var rs = NewRun();
        var player = rs.Players[0];
        W1Pending.Run(nameof(Test_Rooms_RestSiteHealIsThirtyPercentAndCookRemovesTwo), "R3 odds-rewards", () =>
        {
            Assert.True(new HealRestSiteOption(player).OnSelect(rs));
        });
        Assert.Equal(74, player.CurrentHp, "50 + 80 * 0.3");

        var a = AddDeck(rs, "STRIKE_IRONCLAD");
        var b = AddDeck(rs, "STRIKE_IRONCLAD");
        AddDeck(rs, "STRIKE_IRONCLAD");
        var cook = new CookRestSiteOption(player);
        Assert.True(cook.IsEnabled);
        rs.Choice.Answerer = new PicksAnswerer(0, 1);
        Assert.True(cook.OnSelect(rs));
        Assert.False(player.Deck.Contains(a) || player.Deck.Contains(b));
        Assert.Equal(1, player.Deck.Count);
        Assert.Equal(85, player.MaxHp);
        Assert.Equal(79, player.CurrentHp);
    }

    public static void Test_Rooms_TreasureRoomRejectsActIndexOutsideZeroToTwo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TreasureRoom(3));
        var room = new TreasureRoom(1);
        var rs = NewRun();
        rs.CurrentRooms.Add(room);
        W1Pending.Run(nameof(Test_Rooms_TreasureRoomRejectsActIndexOutsideZeroToTwo), "R3 odds-rewards", () =>
        {
            room.Enter(rs);
            Assert.NotNull(room.CurrentRelicId);
            Assert.False(room.ProceedAllowed);
            int gold = rs.Players[0].Gold;
            room.OpenChest(rs);
            Assert.True(rs.Players[0].Gold - gold is >= 42 and <= 52);
            room.TakeRelic(rs);
            Assert.True(room.RelicClaimed);
        });
    }

    public static void Test_Rooms_MerchantInventoryLayout()
    {
        var rs = NewRun();
        W1Pending.Run(nameof(Test_Rooms_MerchantInventoryLayout), "R3 odds-rewards", () =>
        {
            MerchantInventory inventory;
            try
            {
                inventory = MerchantInventory.CreateForNormalMerchant(rs, rs.Players[0]);
            }
            catch (KeyNotFoundException ex) when (ex.Message.StartsWith("No RelicModel registered", StringComparison.Ordinal))
            {
                throw new NotImplementedException("W3 content: " + ex.Message, ex);
            }
            Assert.Equal(5, inventory.CharacterCardEntries.Count);
            Assert.Equal(2, inventory.ColorlessCardEntries.Count);
            Assert.Equal(3, inventory.RelicEntries.Count);
            Assert.Equal(3, inventory.PotionEntries.Count);
            Assert.Equal(1, inventory.CharacterCardEntries.Count(e => e.IsOnSale));
            Assert.Equal(14, inventory.AllEntries.Count);
        });
    }
}
