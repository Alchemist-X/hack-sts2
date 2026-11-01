using Sts2Sim.Core;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class Content_relics_10_1Tests
{
    private static readonly string[] PortedIds =
    {
        "FUR_COAT", "GOLDEN_COMPASS", "LORDS_PARASOL", "WHISPERING_EARRING",
    };

    private static CombatState NewCombat()
    {
        var player = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var run = RunState.CreateStandalone(player, new RunRngSet(777UL), 0, null);
        var cs = new CombatState(run, player, "RELICS_10_1_TEST");
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

    public static void Test_Content_relics_10_1_AllIdsRegistered()
    {
        foreach (var id in PortedIds)
        {
            var relic = RelicRegistry.Create(id);
            Assert.Equal(id, relic.Id, "registry id");
            Assert.Equal(id, DeterministicHash.Slugify(relic.GetType().Name), "slug");
        }
    }

    public static void Test_Content_relics_10_1_EarringEnergyAndCompassUnknowns()
    {
        var cs = NewCombat();
        var earring = Equip(cs, new WhisperingEarring());
        Assert.Equal(4m, earring.ModifyMaxEnergy(cs, cs.Player, 3m), "+1 max energy");
        var compass = Equip(cs, new GoldenCompass());
        var input = new HashSet<RoomType> { RoomType.Monster, RoomType.Event };
        Assert.Equal(2, compass.ModifyUnknownMapPointRoomTypes(cs.Run, input).Count, "other act untouched");
        compass.GoldenPathAct = cs.Run.CurrentActIndex;
        var result = compass.ModifyUnknownMapPointRoomTypes(cs.Run, input);
        Assert.Equal(1, result.Count, "golden act");
        Assert.True(result.Contains(RoomType.Event), "event only");
    }
}
