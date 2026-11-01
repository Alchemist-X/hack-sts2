using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_events_12_1Tests
{
    private static readonly string[] PortedIds =
    {
        "ABYSSAL_BATHS", "BUGSLAYER", "BYRDONIS_NEST", "DOLL_ROOM", "HUNGRY_FOR_MUSHROOMS", "JUNGLE_MAZE_ADVENTURE",
        "LOST_WISP", "SLIPPERY_BRIDGE", "SUNKEN_STATUE", "SUNKEN_TREASURY", "TEA_MASTER", "TRASH_HEAP",
        "INFESTED_AUTOMATON", "RANWID_THE_ELDER", "REFLECTIONS", "RELIC_TRADER", "ROUND_TEA_PARTY", "TABLET_OF_TRUTH",
        "THIS_OR_THAT", "UNREST_SITE", "WELCOME_TO_WONGOS",
    };

    private static RunState NewRun(string seed)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, StartingDecks.IroncladStartingHp, StartingDecks.IroncladStartingHp, StartingDecks.IroncladMaxEnergy);
        var rs = RunState.CreateStandalone(player, new RunRngSet(seed), 0);
        foreach (var id in StartingDecks.Ironclad)
        {
            var card = new CardInstance(CardRegistry.Get(id), rs.AllocateCardInstanceId());
            card.AttachTo(player);
            player.Deck.Add(card);
        }
        rs.InitializeSeed(player, seed);
        player.RelicGrabBag = new RelicGrabBag(refreshAllowed: false);
        player.RelicGrabBag.Populate(RelicRegistry.AllIds.Where(id => RelicCatalog.RarityOf(id) is RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare).ToList(), rs.Rng.UpFront);
        return rs;
    }

    private static EventModel Begin(RunState rs, string eventId)
    {
        var ev = EventRegistry.Create(eventId);
        ev.BeginEvent(rs, rs.Players[0], false);
        return ev;
    }

    public static void Test_Content_events_12_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var ev = EventRegistry.Create(id);
            Assert.Equal(id, ev.Id, "registry id");
        }
    }

    public static void Test_Content_events_12_1_AbyssalBathsLingerScalesDamage()
    {
        var rs = NewRun("BATHS");
        var player = rs.Players[0];
        var ev = (AbyssalBaths)Begin(rs, AbyssalBaths.EventId);
        int maxHp = player.MaxHp;
        ev.ChooseOption(rs, 0);
        Assert.Equal(maxHp + 2, player.MaxHp, "immerse max hp");
        Assert.Equal(maxHp + 2 - 3, player.CurrentHp, "immerse damage 3");
        ev.ChooseOption(rs, 0);
        Assert.Equal(maxHp + 4 - 3 - 4, player.CurrentHp, "linger damage 4");
        Assert.Equal(1, ev.LingerCount, "linger count");
        ev.ChooseOption(rs, 1);
        Assert.True(ev.IsFinished, "exit finishes");
    }

    public static void Test_Content_events_12_1_TabletOfTruthDecipherCostsAndClone()
    {
        var rs = NewRun("TABLET");
        var player = rs.Players[0];
        var ev = (TabletOfTruth)Begin(rs, TabletOfTruth.EventId);
        int maxHp = player.MaxHp;
        ev.ChooseOption(rs, 0);
        Assert.Equal(maxHp - 3, player.MaxHp, "first decipher");
        Assert.Equal(1, player.Deck.Count(c => c.IsUpgraded), "one upgrade");
        Assert.Equal(6m, ev.DecipherMaxHpLoss, "second cost");
        var clone = rs.DeepClone();
        var w1 = new CanonicalWriter();
        ev.WriteCanonical(w1);
        var cloned = ev.CloneFor(clone.Players[0], ev.Rng?.DeepClone());
        var w2 = new CanonicalWriter();
        cloned.WriteCanonical(w2);
        Assert.SequenceEqual(w1.ToArray(), w2.ToArray(), "clone canonical");
        ev.ChooseOption(rs, 1);
        Assert.True(ev.IsFinished, "give up finishes");
        Assert.False(cloned.IsFinished, "clone independent");
    }

    public static void Test_Content_events_12_1_RandomPlayoutsFinish()
    {
        var failures = new List<string>();
        foreach (var id in PortedIds)
        {
            for (int seed = 0; seed < 12; seed++)
            {
                try
                {
                    var rs = NewRun(id + seed);
                    var player = rs.Players[0];
                    player.Gold = 50 + seed * 30;
                    var ev = Begin(rs, id);
                    var pick = new Rng((ulong)seed);
                    for (int step = 0; step < 40 && !ev.IsFinished && !player.IsDead; step++)
                    {
                        var enabled = Enumerable.Range(0, ev.CurrentOptions.Count).Where(i => ev.CurrentOptions[i].IsEnabled).ToList();
                        if (enabled.Count == 0)
                        {
                            failures.Add(id + "/" + seed + ": no enabled option");
                            break;
                        }
                        var clone = ev.CloneFor(player, ev.Rng?.DeepClone());
                        var a = new CanonicalWriter();
                        var b = new CanonicalWriter();
                        ev.WriteCanonical(a);
                        clone.WriteCanonical(b);
                        if (!a.ToArray().SequenceEqual(b.ToArray()))
                        {
                            failures.Add(id + "/" + seed + ": clone canonical differs");
                        }
                        ev.ChooseOption(rs, enabled[pick.NextInt(0, enabled.Count)]);
                    }
                }
                catch (Exception e)
                {
                    failures.Add(id + "/" + seed + ": " + e.GetType().Name + ": " + e.Message);
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(30)));
    }
}
