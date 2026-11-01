using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_events_3456_1Tests
{
    private static readonly string[] PortedIds =
    {
        "AMALGAMATOR", "AROMA_OF_CHAOS", "BRAIN_LEECH", "COLORFUL_PHILOSOPHERS", "DOORS_OF_LIGHT_AND_DARK",
        "DROWNING_BEACON", "ENDLESS_CONVEYOR", "FIELD_OF_MAN_SIZED_HOLES", "GRAVE_OF_THE_FORGOTTEN", "LUMINOUS_CHOIR",
        "MORPHIC_GROVE", "POTION_COURIER", "ROOM_FULL_OF_CHEESE", "SAPPHIRE_SEED", "SELF_HELP_BOOK",
        "SPIRALING_WHIRLPOOL", "SPIRIT_GRAFTER", "STONE_OF_ALL_TIME",
    };

    private sealed class FirstPicksAnswerer : IChoiceAnswerer
    {
        public int Prompts { get; private set; }

        public ChoiceAnswer Answer(ChoicePrompt prompt)
        {
            Prompts++;
            return new ChoiceAnswer(Enumerable.Range(0, prompt.MaxSelect).ToArray(), false);
        }
    }

    private static RunState NewRun(ulong seed = 4242UL)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, 50, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(seed), 0);
        player.PlayerRng = new PlayerRngSet(99UL);
        player.PlayerOdds = new PlayerOddsSet(player.PlayerRng, 0);
        player.RelicGrabBag = new RelicGrabBag(false);
        player.RelicGrabBag.Populate(run, player, run.Rng.UpFront);
        run.SharedRelicGrabBag = new RelicGrabBag(true);
        run.SharedRelicGrabBag.Populate(RelicGrabBag.SharedPoolRelics(run), run.Rng.UpFront);
        run.Odds = new RunOddsSet(run.Rng.UnknownMapPoint);
        player.Gold = 300;
        foreach (var id in StartingDecks.Ironclad)
        {
            var card = new CardInstance(CardRegistry.Get(id), run.AllocateCardInstanceId());
            card.AttachTo(player);
            player.Deck.Add(card);
        }
        run.Choice.Answerer = new FirstPicksAnswerer();
        return run;
    }

    private static EventModel Begin(RunState rs, string id)
    {
        var ev = EventRegistry.Create(id);
        ev.BeginEvent(rs, rs.Players[0], false);
        return ev;
    }

    public static void Test_Content_events_3456_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(EventRegistry.Models.Contains(id), id + " is registered");
            Assert.Equal(id, EventRegistry.Create(id).Id);
        }
    }

    public static void Test_Content_events_3456_1_EveryOptionRuns()
    {
        foreach (var id in PortedIds)
        {
            int optionCount = Begin(NewRun(), id).CurrentOptions.Count;
            for (int o = 0; o < optionCount; o++)
            {
                var rs = NewRun();
                var ev = Begin(rs, id);
                if (!ev.CurrentOptions[o].IsEnabled)
                {
                    continue;
                }
                ev.ChooseOption(rs, o);
                if (!ev.IsFinished && ev.CurrentOptions.Count > 0 && ev.CurrentOptions[0].IsEnabled)
                {
                    ev.ChooseOption(rs, 0);
                }
                var clone = ev.CloneFor(rs.Players[0], ev.Rng?.DeepClone());
                var a = new CanonicalWriter();
                ev.WriteCanonical(a);
                var b = new CanonicalWriter();
                clone.WriteCanonical(b);
                Assert.Equal(StateHasher.Hash(a.WrittenSpan), StateHasher.Hash(b.WrittenSpan), id + " option " + o + ": clone hash");
            }
        }
    }

    public static void Test_Content_events_3456_1_AmalgamatorAndTransform()
    {
        var rs = NewRun();
        var player = rs.Players[0];
        int deckCount = player.Deck.Count;
        var ev = Begin(rs, "AMALGAMATOR");
        ev.ChooseOption(rs, 0);
        Assert.True(ev.IsFinished, "Amalgamator finishes");
        Assert.Equal(deckCount - 1, player.Deck.Count, "two strikes removed, one card added");
        Assert.Equal(1, player.Deck.Count(c => c.Id == "ULTIMATE_STRIKE"), "Ultimate Strike added");

        var rs2 = NewRun();
        var player2 = rs2.Players[0];
        var before = player2.Deck.Select(c => c.InstanceId).ToHashSet();
        var aroma = Begin(rs2, "AROMA_OF_CHAOS");
        aroma.ChooseOption(rs2, 0);
        Assert.Equal(before.Count, player2.Deck.Count, "transform keeps the deck size");
        Assert.Equal(1, player2.Deck.Count(c => !before.Contains(c.InstanceId)), "one new card in the deck");
    }
}
