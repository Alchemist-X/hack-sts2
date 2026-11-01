using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_monsters_2Tests
{
    private const string TestEncounterId = "MONSTERS_2_TEST";

    private static readonly string[] PortedMonsterIds =
    {
        "GAS_BOMB", "GUARDBOT", "HUNTER_KILLER", "INKLET", "KIN_FOLLOWER", "KIN_PRIEST", "LAGAVULIN_MATRIARCH",
        "LIVING_SHIELD", "LOUSE_PROGENITOR", "MAGI_KNIGHT", "MAWLER", "MYSTERIOUS_KNIGHT", "NOISEBOT",
        "OWL_MAGISTRATE", "PHANTASMAL_GARDENER", "PUNCH_CONSTRUCT", "ROCKET", "SCROLL_OF_BITING", "SEWER_CLAM",
        "SKULKING_COLONY", "SLIMED_BERSERKER", "SLITHERING_STRANGLER", "SLUMBERING_BEETLE",
    };

    private static readonly string[] PortedPowerIds =
    {
        "TENDER_POWER", "ASLEEP_POWER", "SLUMBER_POWER", "RAMPART_POWER", "CURL_UP_POWER", "SOAR_POWER",
        "PAPER_CUTS_POWER", "CRAB_RAGE_POWER", "BACK_ATTACK_RIGHT_POWER", "BACK_ATTACK_LEFT_POWER",
        "HARDENED_SHELL_POWER", "SKITTISH_POWER", "SURROUNDED_POWER", "DAMPEN_POWER",
    };

    public static void Test_Content_monsters_2_AllIdsRegistered()
    {
        foreach (var id in PortedMonsterIds)
        {
            Assert.Equal(id, MonsterRegistry.Get(id).Id, "monster registry id");
        }
        foreach (var id in PortedPowerIds)
        {
            Assert.Equal(id, PowerRegistry.Create(id).Id, "power registry id");
        }
    }

    public static void Test_Content_monsters_2_EveryMonsterSurvivesRandomPlay()
    {
        var failures = new List<string>();
        var legal = new List<GameAction>(32);
        foreach (var id in PortedMonsterIds)
        {
            for (int s = 0; s < 3; s++)
            {
                ulong seed = 1000UL + (ulong)s * 7919UL;
                var random = new System.Random((int)seed + id.Length);
                try
                {
                    var sim = CombatSim.Create(seed, StartingDecks.Ironclad, TestEncounterId, s == 2 ? 10 : 0, Spawn(id));
                    int steps = 0;
                    while (!sim.IsTerminal && steps < 200)
                    {
                        sim.LegalActions(legal);
                        if (legal.Count == 0)
                        {
                            throw new InvalidOperationException("a live combat has no legal action");
                        }
                        sim.Step(legal[random.Next(legal.Count)]);
                        steps++;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{id}/{seed}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    public static void Test_Content_monsters_2_LagavulinWakesOnUnblockedDamage()
    {
        var sim = CombatSim.Create(5UL, StartingDecks.Ironclad, TestEncounterId, 0, Spawn("LAGAVULIN_MATRIARCH"));
        var cs = sim.State;
        var lagavulin = cs.Monsters[0];
        Assert.Equal(3, lagavulin.GetPowerAmount<AsleepPower>(), "asleep 3 on spawn");
        Assert.Equal(12, lagavulin.GetPowerAmount<PlatingPower>(), "plating 12 on spawn");
        Assert.Equal(LagavulinMatriarch.SleepMoveId, lagavulin.Ai.NextMove?.Id, "starts asleep");
        CreatureCmd.LoseBlock(cs, lagavulin, lagavulin.Block, null);
        CreatureCmd.Damage(cs, lagavulin, 5m, ValueProp.Move, cs.Player);
        Assert.False(lagavulin.HasPower<AsleepPower>(), "asleep removed");
        Assert.False(lagavulin.HasPower<PlatingPower>(), "plating removed");
        Assert.Equal(MoveStateDef.StunnedMoveId, lagavulin.Ai.NextMove?.Id, "stunned into wake-up");
    }

    public static void Test_Content_monsters_2_SoarHalvesPoweredAttacks()
    {
        var sim = CombatSim.Create(5UL, StartingDecks.Ironclad, TestEncounterId, 0, Spawn("OWL_MAGISTRATE"));
        var cs = sim.State;
        var owl = cs.Monsters[0];
        PowerCmd.Apply<SoarPower>(cs, owl, 1m, owl, null);
        int before = owl.CurrentHp;
        CreatureCmd.Damage(cs, owl, 10m, ValueProp.Move, cs.Player);
        Assert.Equal(before - 5, owl.CurrentHp, "powered attack x0.5");
        before = owl.CurrentHp;
        CreatureCmd.Damage(cs, owl, 10m, ValueProp.Unpowered, cs.Player);
        Assert.Equal(before - 10, owl.CurrentHp, "unpowered damage unchanged");
    }

    private static Action<CombatState> Spawn(string monsterId) => cs =>
    {
        var def = MonsterRegistry.Get(monsterId);
        string? slot = monsterId == "PHANTASMAL_GARDENER" ? PhantasmalGardener.FirstSlot : null;
        int hp = def.MaxInitialHp(cs);
        var monster = new MonsterState(cs.AllocateCreatureId(), def, hp, hp, def.CreateExtraState(), slot);
        cs.Monsters.Add(monster);
    };
}
