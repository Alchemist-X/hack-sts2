using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_monsters_1Tests
{
    private static readonly string[] PortedMonsterIds =
    {
        "ARCHITECT", "ASSASSIN_RUBY_RAIDER", "AXE_RUBY_RAIDER", "BOWLBUG_EGG", "BOWLBUG_NECTAR",
        "BOWLBUG_ROCK", "BOWLBUG_SILK", "BRUTE_RUBY_RAIDER", "BYGONE_EFFIGY", "BYRDONIS",
        "CALCIFIED_CULTIST", "CROSSBOW_RUBY_RAIDER", "CRUSHER", "CUBEX_CONSTRUCT", "DAMP_CULTIST",
        "DEVOTED_SCULPTOR", "ENTOMANCER", "EXOSKELETON", "FAKE_MERCHANT_MONSTER", "FLAIL_KNIGHT",
        "FLYCONID", "FOSSIL_STALKER", "FROG_KNIGHT",
    };

    private static readonly string[] PortedPowerIds =
    {
        "IMBALANCED_POWER", "SLOW_POWER", "TERRITORIAL_POWER", "PERSONAL_HIVE_POWER",
        "HARD_TO_KILL_POWER", "SUCK_POWER", "BACK_ATTACK_LEFT_POWER", "CRAB_RAGE_POWER",
    };

    private const string TestEncounterId = "MONSTERS_1_TEST_ENCOUNTER";

    public static void Test_Content_monsters_1_AllIdsRegistered()
    {
        foreach (var id in PortedMonsterIds)
        {
            Assert.True(MonsterRegistry.TryGet(id, out var def), $"{id} is registered");
            Assert.Equal(id, def.Id, $"{id} round-trips through the registry");
        }
        var powers = new HashSet<string>(PowerRegistry.AllIds, StringComparer.Ordinal);
        foreach (var id in PortedPowerIds)
        {
            Assert.True(powers.Contains(id), $"{id} is registered");
            Assert.Equal(id, PowerRegistry.Create(id).Id, $"{id} round-trips through the registry");
        }
    }

    public static void Test_Content_monsters_1_RandomPlaySoak()
    {
        var failures = new List<string>();
        foreach (var id in PortedMonsterIds)
        {
            for (int s = 0; s < 3; s++)
            {
                ulong seed = 11UL + (ulong)s * 7919UL;
                foreach (var ascension in new[] { 0, 10 })
                {
                    try
                    {
                        RunCase(id, seed, ascension);
                    }
                    catch (NotImplementedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{id}/{seed}/A{ascension}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    public static void Test_Content_monsters_1_BowlbugRockStunsItselfWhenFullyBlocked()
    {
        var sim = CombatSim.Create(5UL, StartingDecks.Ironclad, TestEncounterId, 0, cs => Populate(cs, BowlbugRock.MonsterIdEntry, null));
        var cs = sim.State;
        var rock = cs.Monsters[0];
        Assert.True(rock.HasPower<ImbalancedPower>(), "BowlbugRock spawns with Imbalanced");
        for (int i = 0; i < 6 && !sim.IsTerminal; i++)
        {
            cs = sim.State;
            cs.Player.Block = 999;
            sim.Step(GameAction.EndTurn);
            cs = sim.State;
            if (cs.Monsters.Count > 0 && cs.Monsters[0].Ai.NextMove?.Id == MoveStateDef.StunnedMoveId)
            {
                return;
            }
        }
        Assert.Fail("a fully blocked headbutt stuns the BowlbugRock");
    }

    private static void RunCase(string id, ulong seed, int ascension)
    {
        var sim = CombatSim.Create(seed, StartingDecks.Ironclad, TestEncounterId, ascension, cs => Populate(cs, id, id == Exoskeleton.MonsterIdEntry ? Exoskeleton.FirstSlot : null));
        var random = new System.Random(unchecked((int)seed));
        var legal = new List<GameAction>(32);
        for (int steps = 0; steps < 200 && !sim.IsTerminal; steps++)
        {
            sim.LegalActions(legal);
            if (legal.Count == 0)
            {
                throw new InvalidOperationException("a live combat has no legal action");
            }
            sim.Step(legal[random.Next(legal.Count)]);
        }
    }

    private static void Populate(CombatState cs, string id, string? slot)
    {
        cs.Monsters.Add(MonsterSpawner.Spawn(cs, new MonsterSpawn(MonsterRegistry.Get(id), slot)));
    }
}
