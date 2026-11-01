using Sts2Sim.Core;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_5_1Tests
{
    private static readonly string[] PortedIds =
    {
        "BONE_FLUTE", "BOUND_PHYLACTERY", "BYRDPIP", "CRACKED_CORE", "DIVINE_DESTINY", "DIVINE_RIGHT",
        "EMOTION_CHIP", "FENCING_MANUAL", "GALACTIC_DUST", "GOLD_PLATED_CABLES", "INFUSED_CORE", "LUNAR_PASTRY",
        "METRONOME", "MINI_REGENT", "PAELS_LEGION", "PHYLACTERY_UNBOUND", "RUNIC_CAPACITOR", "SYMBIOTIC_VIRUS",
    };

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "DEFECT", 80, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(777UL), 0, null);
        var cs = new CombatState(run, player, "RELICS_5_1_TEST");
        cs.IsInProgress = true;
        cs.Monsters.Add(new MonsterState(1, MonsterRegistry.Get("NIBBIT"), 50, 50, null, null));
        cs.NextCreatureId = 2;
        return cs;
    }

    private static T Equip<T>(CombatState cs, T relic) where T : RelicModel
    {
        relic.AttachTo(cs.Player);
        cs.Player.Relics.Add(relic);
        return relic;
    }

    public static void Test_Content_relics_5_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id, "registry id");
        }
    }

    public static void Test_Content_relics_5_1_GalacticDustBlocksPerTenStarsAndKeepsRemainder()
    {
        var cs = NewCombat();
        var dust = Equip(cs, new GalacticDust());
        dust.AfterStarsSpent(cs, 7, cs.Player);
        Assert.Equal(0, cs.Player.Block, "below threshold");
        dust.AfterStarsSpent(cs, 15, cs.Player);
        Assert.Equal(20, cs.Player.Block, "floor(22/10)*10");
        Assert.Equal(2, dust.StarsSpent, "remainder kept");
    }

    public static void Test_Content_relics_5_1_CapacitorInfusedCoreAndCables()
    {
        var cs = NewCombat();
        var participants = new CreatureState[] { cs.Player };
        var capacitor = Equip(cs, new RunicCapacitor());
        var core = Equip(cs, new InfusedCore());
        var cables = Equip(cs, new GoldPlatedCables());
        capacitor.AfterSideTurnStart(cs, CombatSide.Player, participants);
        core.AfterSideTurnStart(cs, CombatSide.Player, participants);
        var orbs = cs.Player.OrbQueue.Orbs;
        Assert.Equal(3, orbs.Count, "three lightning channelled into three slots");
        Assert.True(orbs[0] is LightningOrb, "lightning");
        Assert.Equal(LightningOrb.BasePassiveValue + 1m, orbs[0].PassiveVal(cs), "+1 lightning value");
        Assert.Equal(2, cables.ModifyOrbPassiveTriggerCounts(cs, orbs[0], 1), "first orb triggers twice");
        Assert.Equal(1, cables.ModifyOrbPassiveTriggerCounts(cs, orbs[1], 1), "other orbs unchanged");
    }
}
