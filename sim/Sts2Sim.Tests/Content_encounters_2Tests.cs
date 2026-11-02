using Sts2Sim.Core;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;

namespace Sts2Sim.Tests;

public static class Content_encounters_2Tests
{
    private static readonly string[] PortedIds =
    {
        "SOUL_NEXUS_ELITE", "SPINY_TOAD_NORMAL", "TERROR_EEL_ELITE", "THE_ARCHITECT_EVENT_ENCOUNTER",
        "THE_INSATIABLE_BOSS", "THE_KIN_BOSS", "THE_LOST_AND_FORGOTTEN_NORMAL", "TUNNELER_WEAK",
        "TURRET_OPERATOR_WEAK", "AEONGLASS_BOSS", "CHOMPERS_NORMAL", "DENSE_VEGETATION_EVENT_ENCOUNTER",
        "HAUNTED_SHIP_NORMAL", "MECHA_KNIGHT_ELITE", "MYTES_NORMAL", "TUNNELER_NORMAL", "VANTOM_BOSS",
        "CEREMONIAL_BEAST_BOSS", "GLOBE_HEAD_NORMAL", "INFESTED_PRISMS_ELITE", "KNIGHTS_ELITE", "QUEEN_BOSS",
        "VINE_SHAMBLER_NORMAL", "AXEBOTS_NORMAL", "FABRICATOR_NORMAL", "LIVING_FOG_NORMAL", "OVICOPTER_NORMAL",
        "PHROG_PARASITE_ELITE", "TWO_TAILED_RATS_NORMAL", "DECIMILLIPEDE_ELITE", "FOGMOG_NORMAL",
        "TEST_SUBJECT_BOSS", "THE_OBSCURA_NORMAL", "WATERFALL_GIANT_BOSS", "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER",
        "BATTLEWORN_DUMMY_EVENT_V2_ENCOUNTER", "BATTLEWORN_DUMMY_EVENT_V3_ENCOUNTER", "GREMLIN_MERC_NORMAL",
        "THIEVING_HOPPER_WEAK", "KNOWLEDGE_DEMON_BOSS",
    };

    public static void Test_Encounters2_EveryPortedIdIsRegisteredWithItsRoomAndMonsters()
    {
        foreach (var id in PortedIds)
        {
            Assert.True(EncounterRegistry.TryGet(id, out var def), $"{id} is registered");
            Assert.True(def.AllPossibleMonsters.Count > 0, $"{id} lists its possible monsters");
        }
        Assert.Equal(RoomType.Boss, EncounterRegistry.Get("QUEEN_BOSS").RoomType);
        Assert.Equal(RoomType.Elite, EncounterRegistry.Get("KNIGHTS_ELITE").RoomType);
        Assert.True(EncounterRegistry.Get("TUNNELER_WEAK").IsWeak, "TunnelerWeak is weak");
        Assert.False(new BattlewornDummyEventV1Encounter().ShouldGiveRewards, "dummy fights give no rewards");
        Assert.Equal(2, EncounterRegistry.Get("TUNNELER_NORMAL").Tags.Count);
        Assert.Equal("fabricator", EncounterRegistry.Get("FABRICATOR_NORMAL").Slots[2]);
        Assert.Equal("wriggler3", PhrogParasiteElite.GetWrigglerSlotName(2));
    }

    public static void Test_Encounters2_RotatingStarterMovesFollowOneEncounterRngDraw()
    {
        W1Pending.Run("Encounters2 rotating starters", "monsters (TWO_TAILED_RAT, DECIMILLIPEDE_SEGMENT_*)", () =>
        {
            foreach (var id in new[] { "TWO_TAILED_RAT", "DECIMILLIPEDE_SEGMENT_FRONT", "DECIMILLIPEDE_SEGMENT_MIDDLE", "DECIMILLIPEDE_SEGMENT_BACK" })
            {
                if (!MonsterRegistry.TryGet(id, out _))
                {
                    throw new NotImplementedException($"monster {id} not ported in this worktree");
                }
            }
            var rats = EncounterRegistry.Get("TWO_TAILED_RATS_NORMAL").GenerateMonsters(null!, new Rng(7));
            Assert.Equal(3, rats.Count);
            Assert.Equal("third", rats[0].SlotName);
            Assert.Equal("fifth", rats[2].SlotName);
            var segments = EncounterRegistry.Get("DECIMILLIPEDE_ELITE").GenerateMonsters(null!, new Rng(7));
            Assert.Equal("DECIMILLIPEDE_SEGMENT_BACK", segments[2].Def.Id);
            Assert.Equal("segment3", segments[2].SlotName);
        });
    }
}
