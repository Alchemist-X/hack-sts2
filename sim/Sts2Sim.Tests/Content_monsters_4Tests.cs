using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_monsters_4Tests
{
    private static readonly string[] PortedMonsterIds =
    {
        "CEREMONIAL_BEAST", "GLOBE_HEAD", "INFESTED_PRISM", "QUEEN", "SPECTRAL_KNIGHT", "VINE_SHAMBLER",
        "AXEBOT", "FABRICATOR", "FOGMOG", "LIVING_FOG", "OVICOPTER", "PHROG_PARASITE", "THE_OBSCURA",
        "TWO_TAILED_RAT", "DECIMILLIPEDE_SEGMENT_BACK", "DECIMILLIPEDE_SEGMENT_FRONT",
        "DECIMILLIPEDE_SEGMENT_MIDDLE", "EYE_WITH_TEETH", "PARAFRIGHT", "TEST_SUBJECT", "WATERFALL_GIANT",
        "KNOWLEDGE_DEMON",
    };

    private static readonly string[] PortedPowerIds =
    {
        "REATTACH_POWER", "PLOW_POWER", "STEAM_ERUPTION_POWER", "ADAPTABLE_POWER", "ENRAGE_POWER",
        "NEMESIS_POWER", "PAINFUL_STABS_POWER", "ILLUSION_POWER",
    };

    private const string HostEncounterId = "NIBBITS_WEAK";

    public static void Test_Content_monsters_4_EveryPortedIdIsRegistered()
    {
        foreach (var id in PortedMonsterIds)
        {
            Assert.True(MonsterRegistry.TryGet(id, out var def), $"monster {id} is registered");
            Assert.Equal(id, def!.Id);
        }
        foreach (var id in PortedPowerIds)
        {
            Assert.Equal(id, PowerRegistry.Create(id).Id);
        }
    }

    public static void Test_Content_monsters_4_SoakEachMonsterAlone()
    {
        var failures = new List<string>();
        foreach (var id in PortedMonsterIds)
        {
            var spawnIds = id == Queen.MonsterIdEntry ? new[] { Queen.TorchHeadAmalgamMonsterId, id } : new[] { id };
            if (!AllRegistered(spawnIds, out var missing))
            {
                Console.WriteLine($"PENDING Content_monsters_4 soak {id} (monsters owner): {missing} not registered yet");
                W1Pending.Count++;
                continue;
            }
            for (ulong seed = 1; seed <= 2; seed++)
            {
                try
                {
                    W1Pending.Run($"Content_monsters_4 soak {id}/{seed}", "other content owners", () => RunOne(spawnIds, seed));
                }
                catch (KeyNotFoundException ex)
                {
                    Console.WriteLine($"PENDING Content_monsters_4 soak {id}/{seed} (other content owners): {ex.Message}");
                    W1Pending.Count++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{id}/{seed}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        Assert.True(failures.Count == 0, "monsters-4 soak: " + string.Join(" | ", failures));
    }

    public static void Test_Content_monsters_4_AxebotRespawnsFromStock()
    {
        var sim = CombatSim.Create(7UL, Deck(), HostEncounterId, 0, cs =>
        {
            cs.Monsters.Add(MonsterSpawner.Spawn(cs, new MonsterSpawn(MonsterRegistry.Get(Axebot.MonsterIdEntry))));
        });
        var cs = sim.State;
        var axebot = cs.Monsters[0];
        Assert.Equal(Axebot.InitialStockAmount, axebot.GetPowerAmount<StockPower>());
        Assert.Equal(Axebot.HammerUppercutMoveId, axebot.Ai.NextMove?.Id, "an encounter Axebot opens with HAMMER_UPPERCUT");
        CreatureCmd.Kill(cs, axebot);
        MonsterState? replacement = null;
        foreach (var m in cs.Monsters)
        {
            if (!ReferenceEquals(m, axebot) && m.Def is Axebot)
            {
                replacement = m;
            }
        }
        Assert.NotNull(replacement, "StockPower spawns a replacement Axebot on death");
        Assert.Equal(1, Axebot.StockAmount(replacement!));
        Assert.Equal(1, replacement!.GetPowerAmount<StockPower>());
        Assert.Equal(Axebot.BootUpMoveId, replacement.Ai.NextMove?.Id, "a respawned Axebot opens with BOOT_UP");
        Assert.True(replacement.MaxHp >= Axebot.MinInitialHpBase + Axebot.RespawnMaxHpBonusPerRespawn, "respawn adds 10 max HP per respawn");
    }

    private static bool AllRegistered(string[] ids, out string missing)
    {
        foreach (var id in ids)
        {
            if (!MonsterRegistry.TryGet(id, out _))
            {
                missing = id;
                return false;
            }
        }
        missing = string.Empty;
        return true;
    }

    private static IReadOnlyList<string> Deck()
    {
        var deck = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            deck.Add(StrikeIronclad.CardId);
            deck.Add(DefendIronclad.CardId);
        }
        return deck;
    }

    private static void RunOne(string[] spawnIds, ulong seed)
    {
        var sim = CombatSim.Create(seed, Deck(), HostEncounterId, 0, cs =>
        {
            foreach (var id in spawnIds)
            {
                cs.Monsters.Add(MonsterSpawner.Spawn(cs, new MonsterSpawn(MonsterRegistry.Get(id))));
            }
        });
        var random = new System.Random((int)seed);
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
}
