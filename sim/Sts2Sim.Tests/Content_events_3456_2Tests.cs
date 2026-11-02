using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_events_3456_2Tests
{
    private static readonly string[] PortedIds =
    {
        "SYMBIOTE", "THE_FUTURE_OF_POTIONS", "THE_LEGENDS_WERE_TRUE", "TRIAL", "WATERLOGGED_SCRIPTORIUM",
        "WELLSPRING", "WHISPERING_HOLLOW", "WOOD_CARVINGS", "ZEN_WEAVER", "DENSE_VEGETATION", "PUNCH_OFF",
        "THE_LANTERN_KEY", "WAR_HISTORIAN_REPY", "COLOSSAL_FLOWER",
    };

    private sealed class FirstPicksAnswerer : IChoiceAnswerer
    {
        public int Prompts { get; private set; }

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            Prompts++;
            var picks = new int[prompt.MinSelect > 0 ? prompt.MinSelect : 1];
            for (int i = 0; i < picks.Length; i++)
            {
                picks[i] = i;
            }
            return new ChoiceAnswer(picks, false);
        }
    }

    private static RunState NewRun(ulong seed)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 60, 80, 3);
        var rs = RunState.CreateStandalone(player, new RunRngSet(seed), 0);
        player.PlayerRng = new PlayerRngSet(seed + 1);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, 0);
        player.RelicGrabBag = new RelicGrabBag(false);
        player.RelicGrabBag.Populate(rs, player, rs.Rng.UpFront);
        rs.SharedRelicGrabBag = new RelicGrabBag(true);
        rs.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(rs), rs.Rng.UpFront);
        rs.Odds = new RunOddsSet(rs.Rng.UnknownMapPoint);
        foreach (var id in StartingDecks.Ironclad)
        {
            var card = new CardInstance(CardRegistry.Get(id), rs.AllocateCardInstanceId());
            card.AttachTo(player);
            player.Deck.Add(card);
        }
        player.Gold = 300;
        return rs;
    }

    private static EventModel Begin(RunState rs, string id)
    {
        var ev = EventRegistry.Create(id);
        ev.BeginEvent(rs, rs.Players[0], false);
        return ev;
    }

    public static void Test_Events3456_2_EveryPortedIdIsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(EventRegistry.Models.Contains(id), $"{id} is registered");
            Assert.Equal(id, EventRegistry.Create(id).Id);
        }
    }

    public static void Test_Events3456_2_FirstOptionPlaythroughOnEveryEvent()
    {
        foreach (var id in PortedIds)
        {
            W1Pending.Run("Events3456_2.FirstOptionPlaythrough." + id, "W3 cards", () => PlayFirstOptions(id));
        }
    }

    private static void PlayFirstOptions(string id)
    {
        {
            var rs = NewRun(7UL);
            rs.Choice.Answerer = new FirstPicksAnswerer();
            var ev = Begin(rs, id);
            for (int guard = 0; guard < 8 && !ev.IsFinished; guard++)
            {
                int index = -1;
                for (int i = 0; i < ev.CurrentOptions.Count; i++)
                {
                    if (ev.CurrentOptions[i].IsEnabled)
                    {
                        index = i;
                        break;
                    }
                }
                Assert.True(index >= 0, $"{id} has an enabled option");
                if (ev.CurrentOptions[index].TextKey.EndsWith(".FIGHT", StringComparison.Ordinal))
                {
                    break;
                }
                var clone = ev.CloneFor(rs.Players[0], ev.Rng?.DeepClone());
                Assert.Equal(StateHasher.Hash(ev), StateHasher.Hash(clone), $"{id} clone hash");
                ev.ChooseOption(rs, index);
            }
        }
    }

    public static void Test_Events3456_2_ColossalFlowerDigsToPollinousCore()
    {
        var rs = NewRun(11UL);
        var player = rs.Players[0];
        var ev = (ColossalFlower)Begin(rs, ColossalFlower.EventId);
        ev.ChooseOption(rs, 1);
        ev.ChooseOption(rs, 1);
        Assert.Equal(2, ev.NumberOfDigs);
        Assert.Equal(60 - 5 - 6, player.CurrentHp);
        ev.ChooseOption(rs, 1);
        Assert.Equal(60 - 5 - 6 - 7, player.CurrentHp);
        Assert.True(ev.IsFinished);
        Assert.True(player.Relics.Any(r => r.Id == ColossalFlower.PollinousCoreId), "Pollinous Core obtained");
        var rs2 = NewRun(11UL);
        var ev2 = Begin(rs2, ColossalFlower.EventId);
        ev2.ChooseOption(rs2, 1);
        ev2.ChooseOption(rs2, 0);
        Assert.Equal(300 + 75, rs2.Players[0].Gold, "second prize is 75 gold (IL _prizeCosts {35,75,135})");
    }

    public static void Test_Events3456_2_WoodCarvingsBirdTransformsBasicInDeck()
    {
        var rs = NewRun(13UL);
        rs.Choice.Answerer = new FirstPicksAnswerer();
        var player = rs.Players[0];
        int deckCount = player.Deck.Count;
        var first = player.Deck.First(c => c.Rarity == CardRarity.Basic);
        var ev = Begin(rs, WoodCarvings.EventId);
        ev.ChooseOption(rs, 0);
        Assert.True(ev.IsFinished);
        Assert.Equal(deckCount, player.Deck.Count);
        Assert.False(player.Deck.Contains(first), "the picked basic card left the deck");
        Assert.Equal(WoodCarvings.PeckId, player.Deck[player.Deck.Count - 1].Id);
        Assert.True(first.HasBeenRemovedFromState);
    }

    public static void Test_Events3456_2_ZenWeaverRemovesThenPays()
    {
        var rs = NewRun(17UL);
        var answerer = new FirstPicksAnswerer();
        rs.Choice.Answerer = answerer;
        var player = rs.Players[0];
        int deckCount = player.Deck.Count;
        var ev = Begin(rs, ZenWeaver.EventId);
        Assert.True(ev.CurrentOptions[2].IsEnabled, "250 gold unlocks Arachnid Acupuncture");
        ev.ChooseOption(rs, 2);
        Assert.Equal(1, answerer.Prompts);
        Assert.Equal(deckCount - 2, player.Deck.Count);
        Assert.Equal(300 - ZenWeaver.ArachnidAcupunctureCost, player.Gold);
    }
}
