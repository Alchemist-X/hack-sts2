using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_leftoversTests
{
    public static readonly string[] PortedIds =
    {
        "FORBIDDEN_GRIMOIRE_POWER", "HEIST_POWER", "IMPROVEMENT_POWER", "ROYALTIES_POWER", "SURPRISE_POWER",
        "SWIPE_POWER", "THIEVERY_POWER", "AGGRESSION", "FORBIDDEN_GRIMOIRE", "ROYALTIES", "MAD_SCIENCE",
        "GREMLIN_MERC", "THIEVING_HOPPER", "FAT_GREMLIN", "BATTLE_FRIEND_V1", "BATTLE_FRIEND_V2", "BATTLE_FRIEND_V3",
        "MEAL_TICKET", "TINKER_TIME", "BATTLEWORN_DUMMY", "THIEVING_HOPPER_WEAK",
        "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER"
    };

    private static readonly CardType[] SoakTypes = { CardType.Attack, CardType.Skill, CardType.Power };

    public static void ConfigureTinkerTimeForSoak(CombatState cs)
    {
        var player = cs.Player;
        foreach (var card in player.Deck)
        {
            ConfigureSoakMadScience(card);
        }
        foreach (var pile in player.AllPiles)
        {
            foreach (var card in pile.Cards)
            {
                if (card.Def is not MadScience || MadScience.GetTinkerTimeType(card) != CardType.None)
                {
                    continue;
                }
                var deckVersion = card.FindDeckVersion();
                if (deckVersion != null && MadScience.GetTinkerTimeType(deckVersion) != CardType.None)
                {
                    MadScience.SetTinkerTimeType(card, MadScience.GetTinkerTimeType(deckVersion));
                    MadScience.SetTinkerTimeRider(card, MadScience.GetTinkerTimeRider(deckVersion));
                }
                else
                {
                    ConfigureSoakMadScience(card);
                }
            }
        }
    }

    private static void ConfigureSoakMadScience(CardInstance card)
    {
        if (card.Def is not MadScience || MadScience.GetTinkerTimeType(card) != CardType.None)
        {
            return;
        }
        var type = SoakTypes[card.InstanceId % SoakTypes.Length];
        MadScience.SetTinkerTimeType(card, type);
        MadScience.SetTinkerTimeRider(card, TinkerTime.RidersFor(type)[card.InstanceId / SoakTypes.Length % 3]);
    }

    public static void Test_Content_leftovers_AllPortedIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            bool found = PowerRegistry.AllIds.Contains(id) || CardRegistry.TryGet(id, out _) || MonsterRegistry.TryGet(id, out _)
                || RelicRegistry.Models.Contains(id) || EventRegistry.Models.Contains(id) || EncounterRegistry.TryGet(id, out _);
            Assert.True(found, $"{id} is registered");
        }
        Assert.False(CardRegistry.Get(Aggression.CardId) is UnportedCardDef, "AGGRESSION is a ported def");
        Assert.True(CardCatalog.IsPorted(Aggression.CardId));
    }

    private static CombatState NewCombat(string encounterId, int gold, params string[] deck)
    {
        var ids = deck.Length > 0 ? deck : new[] { StrikeIronclad.CardId, StrikeIronclad.CardId, StrikeIronclad.CardId, StrikeIronclad.CardId, StrikeIronclad.CardId, DefendIronclad.CardId };
        var sim = CombatSim.Create(7731UL, ids, encounterId);
        var cs = sim.State;
        cs.Player.Gold = gold;
        return cs;
    }

    private static CombatState NewMercCombat(int gold)
    {
        var ids = new[] { StrikeIronclad.CardId, StrikeIronclad.CardId, StrikeIronclad.CardId, StrikeIronclad.CardId, StrikeIronclad.CardId, DefendIronclad.CardId };
        var sim = CombatSim.Create(7731UL, ids, GremlinMercNormal.EncounterIdEntry);
        var state = sim.State;
        state.Player.Gold = gold;
        return state;
    }

    private static CombatRoom PushCombatRoom(CombatState cs)
    {
        var room = new CombatRoom(EncounterRegistry.Get(cs.EncounterId));
        cs.Run.CurrentRooms.Add(room);
        return room;
    }

    public static void Test_Content_leftovers_GremlinMercStealsGoldIntoThievery()
    {
        var cs = NewMercCombat(30);
        var merc = cs.Monsters.Single(m => m.Def is GremlinMerc);
        var thievery = merc.Powers.OfType<ThieveryPower>().Single();
        Assert.Equal(20, thievery.Amount, "THIEVERY 20");
        Assert.Equal(cs.Player.CombatId, thievery.TargetId, "targets the player");
        Assert.True(merc.Powers.OfType<SurprisePower>().Any(), "SURPRISE applied");
        thievery.Steal(cs);
        Assert.Equal(10, cs.Player.Gold, "20 stolen");
        Assert.Equal(20, thievery.Gold, "gold var +20");
        thievery.Steal(cs);
        Assert.Equal(0, cs.Player.Gold, "min(20, 10) stolen");
        Assert.Equal(30, thievery.Gold, "gold var +10");
        thievery.Steal(cs);
        Assert.Equal(30, thievery.Gold, "nothing to steal at 0 gold");
    }

    public static void Test_Content_leftovers_SurpriseSpawnsGremlinsWithoutTheft()
    {
        var cs = NewMercCombat(0);
        var merc = cs.Monsters.Single(m => m.Def is GremlinMerc);
        CreatureCmd.Kill(cs, merc);
        Assert.True(cs.Monsters.Any(m => m.Def is FatGremlin && m.SlotName == SurprisePower.FatSlot), "fat gremlin added");
        Assert.True(cs.Monsters.Any(m => m.Def is SneakyGremlin && m.SlotName == SurprisePower.SneakySlot), "sneaky gremlin added");
        var fat = cs.Monsters.Single(m => m.Def is FatGremlin);
        Assert.False(fat.Powers.OfType<HeistPower>().Any(), "no HEIST when nothing was stolen (PowerCmd.Apply amount 0)");
        Assert.True(cs.IsInProgress, "combat continues");
    }

    public static void Test_Content_leftovers_SurpriseHandsStolenGoldToFatGremlinHeist()
    {
        var cs = NewMercCombat(50);
        var merc = cs.Monsters.Single(m => m.Def is GremlinMerc);
        merc.Powers.OfType<ThieveryPower>().Single().Steal(cs);
        CreatureCmd.Kill(cs, merc);
        var heist = cs.Monsters.Single(m => m.Def is FatGremlin).Powers.OfType<HeistPower>().Single();
        Assert.Equal(20, heist.Amount, "HEIST carries the stolen 20");
        Assert.Equal(cs.Player.CombatId, heist.TargetId, "HEIST targets the robbed player");
        Assert.True(GremlinMercNormal.GoldWasStolen(cs), "SURPRISE marks the encounter's gold as stolen");
    }

    public static void Test_Content_leftovers_HeistReturnsGoldAsReward()
    {
        var cs = NewMercCombat(0);
        var room = PushCombatRoom(cs);
        var merc = cs.Monsters.Single(m => m.Def is GremlinMerc);
        CreatureCmd.Kill(cs, merc);
        var fat = cs.Monsters.Single(m => m.Def is FatGremlin);
        PowerCmd.Apply(cs, new HeistPower { TargetId = cs.Player.CombatId }, fat, 17m, merc, null);
        CreatureCmd.Kill(cs, fat);
        var reward = room.ExtraRewards.OfType<GoldReward>().Single();
        Assert.Equal(17, reward.Amount, "stolen gold back");
        Assert.True(reward.WasGoldStolenBack);
    }

    public static void Test_Content_leftovers_ThievingHopperSwipesDeckCardAndReturnsIt()
    {
        var cs = NewCombat(ThievingHopperWeak.EncounterIdEntry, 0);
        var room = PushCombatRoom(cs);
        var hopper = cs.Monsters.Single(m => m.Def is ThievingHopper);
        int deckBefore = cs.Player.Deck.Count;
        var move = MonsterPowerCoupling.Move(hopper, "ThieveryMove", "test");
        move(cs, hopper, new CreatureState[] { cs.Player });
        Assert.Equal(deckBefore - 1, cs.Player.Deck.Count, "deck version removed");
        var swipe = hopper.Powers.OfType<SwipePower>().Single();
        Assert.Equal(cs.Player.CombatId, swipe.TargetId);
        var stolen = swipe.StolenDeckVersion!;
        Assert.True(stolen.HasBeenRemovedFromState);
        var clone = cs.DeepClone();
        Assert.Equal(StateHasher.Hash(cs), StateHasher.Hash(clone), "swipe state clones hash-equal");
        CreatureCmd.Kill(cs, hopper);
        var reward = room.ExtraRewards.OfType<SpecialCardReward>().Single();
        Assert.True(ReferenceEquals(stolen, reward.Card), "the stolen deck card is offered back");
        Assert.False(stolen.HasBeenRemovedFromState, "RunState.AddCard clears HasBeenRemovedFromState");
        Assert.Equal(ThievingHopperWeak.EncounterIdEntry, reward.CustomDescriptionEncounterSourceId);
    }

    public static void Test_Content_leftovers_FlutterAndEscapeToggleHovering()
    {
        var cs = NewCombat(ThievingHopperWeak.EncounterIdEntry, 0);
        var hopper = cs.Monsters.Single(m => m.Def is ThievingHopper);
        MonsterPowerCoupling.Move(hopper, "FlutterMove", "test")(cs, hopper, Array.Empty<CreatureState>());
        Assert.True(ThievingHopper.GetExtra(hopper).IsHovering);
        Assert.Equal(5, hopper.GetPower(FlutterPower.PowerId)!.Amount);
    }

    public static void Test_Content_leftovers_RoyaltiesAndGrimoireAddRewardsAfterCombat()
    {
        var cs = NewCombat("NIBBITS_WEAK", 0);
        var room = PushCombatRoom(cs);
        var player = cs.Player;
        PowerCmd.Apply<RoyaltiesPower>(cs, player, 30m, player, null);
        PowerCmd.Apply<ForbiddenGrimoirePower>(cs, player, 2m, player, null);
        Hook.AfterCombatEnd(cs.Run, cs, null);
        Assert.Equal(30, room.ExtraRewards.OfType<GoldReward>().Single().Amount, "royalties gold");
        Assert.Equal(2, room.ExtraRewards.OfType<CardRemovalReward>().Count(), "one removal per grimoire stack");
    }

    public static void Test_Content_leftovers_ImprovementUpgradesRandomDeckCards()
    {
        var cs = NewCombat("NIBBITS_WEAK", 0);
        var player = cs.Player;
        PowerCmd.Apply<ImprovementPower>(cs, player, 2m, player, null);
        Hook.AfterCombatEnd(cs.Run, cs, null);
        Assert.Equal(2, player.Deck.Count(c => c.UpgradeLevel > 0), "two deck cards upgraded");
    }

    public static void Test_Content_leftovers_AggressionAppliesPowerAndUpgradesToInnate()
    {
        var cs = NewCombat("NIBBITS_WEAK", 0);
        var player = cs.Player;
        var card = cs.CreateCard(CardRegistry.Get(Aggression.CardId), player);
        CardPileCmd.Add(cs, card, PileType.Hand);
        card.OnPlay(cs, new CardPlay(cs, card, null));
        Assert.Equal(1, player.GetPower(AggressionPower.PowerId)!.Amount);
        var deckCard = new CardInstance(CardRegistry.Get(Aggression.CardId), cs.Run.AllocateCardInstanceId());
        deckCard.Upgrade();
        Assert.True(deckCard.Keywords(null).Contains(CardKeyword.Innate), "upgrade adds Innate");
    }

    public static void Test_Content_leftovers_TinkerTimeAddsConfiguredMadScience()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(4242UL), 0, null);
        var ev = EventRegistry.Create(TinkerTime.EventId);
        ev.BeginEvent(rs, player, false);
        Assert.Equal(1, ev.CurrentOptions.Count);
        ev.ChooseOption(rs, 0);
        Assert.Equal(2, ev.CurrentOptions.Count, "two of three card types offered");
        ev.ChooseOption(rs, 0);
        var tinker = (TinkerTime)ev;
        Assert.Equal(2, ev.CurrentOptions.Count, "two riders offered");
        Assert.True(tinker.OfferedRiders.All(r => TinkerTime.RidersFor(tinker.ChosenCardType).Contains(r)), "riders belong to the chosen type");
        var rider = tinker.OfferedRiders[1];
        ev.ChooseOption(rs, 1);
        Assert.True(ev.IsFinished);
        var mad = player.Deck.Single(c => c.Def is MadScience);
        Assert.Equal(tinker.ChosenCardType, MadScience.GetTinkerTimeType(mad));
        Assert.Equal(rider, MadScience.GetTinkerTimeRider(mad));
        Assert.Equal(tinker.ChosenCardType, mad.Type, "card type follows TinkerTimeType");
    }

    public static void Test_Content_leftovers_MadScienceViolenceAttackHitsThreeTimes()
    {
        var cs = NewCombat("NIBBITS_WEAK", 0);
        var player = cs.Player;
        var card = cs.CreateCard(CardRegistry.Get(MadScience.CardId), player);
        MadScience.SetTinkerTimeType(card, CardType.Attack);
        MadScience.SetTinkerTimeRider(card, MadScience.RiderEffect.Violence);
        CardPileCmd.Add(cs, card, PileType.Hand);
        var target = cs.Monsters[0];
        int hp = target.CurrentHp;
        target.Block = 0;
        card.OnPlay(cs, new CardPlay(cs, card, target));
        Assert.Equal(Math.Max(0, hp - 36), target.CurrentHp, "3 x 12");
    }

    public static void Test_Content_leftovers_BattlewornDummyOffersThreeSettings()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(5151UL), 0, null);
        player.PlayerRng = new PlayerRngSet(5152UL);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, 0);
        player.RelicGrabBag = new RelicGrabBag(false);
        player.RelicGrabBag.Populate(rs, player, rs.Rng.UpFront);
        rs.SharedRelicGrabBag = new RelicGrabBag(true);
        rs.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(rs), rs.Rng.UpFront);
        var ev = EventRegistry.Create(BattlewornDummy.EventId);
        ev.BeginEvent(rs, player, false);
        Assert.Equal(3, ev.CurrentOptions.Count);
        Assert.Equal("BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_3", ev.CurrentOptions[2].TextKey);
        int before = rs.RewardsStack.Count;
        ev.Resume(rs, new CombatRoom(EncounterRegistry.Get(BattlewornDummy.V3EncounterId)));
        Assert.True(ev.IsFinished);
        Assert.Equal(before + 1, rs.RewardsStack.Count, "V3 victory offers a relic reward");
    }

    public static void Test_Content_leftovers_BattlewornDummyRanOutOfTimeReadsTheFinishedCombat()
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 80, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(5151UL), 0, null);
        var ev = EventRegistry.Create(BattlewornDummy.EventId);
        ev.BeginEvent(rs, player, false);
        var room = new CombatRoom(EncounterRegistry.Get(BattlewornDummy.V3EncounterId));
        var cs = CombatLoop.CreateCombat(5UL, Enumerable.Repeat(StrikeIronclad.CardId, 10).ToList(), BattlewornDummy.V3EncounterId, 0);
        BattlewornDummyEventEncounter.SetRanOutOfTime(cs, true);
        room.OnCombatEnded(rs, cs);
        BattlewornDummyEventEncounter.SetRanOutOfTime(cs, false);
        Assert.True(room.EncounterExtra is BattlewornDummyEventEncounter.Extra { RanOutOfTime: true }, "the room keeps a copy of the finished combat's encounter state");
        int before = rs.RewardsStack.Count;
        ev.Resume(rs, room);
        Assert.True(ev.IsFinished);
        Assert.Equal(before, rs.RewardsStack.Count, "DEFEAT page offers nothing");
        Assert.Equal(StateHasher.Hash(room.CloneFor(rs)), StateHasher.Hash(room), "cloned and hashed");
    }
}
