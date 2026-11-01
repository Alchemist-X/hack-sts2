using Sts2Sim.Core;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;

namespace Sts2Sim.Tests;

public static class Content_encounters_1Tests
{
    private static readonly string[] Ids =
    {
        "BOWLBUGS_NORMAL", "BOWLBUGS_WEAK", "BYGONE_EFFIGY_ELITE", "BYRDONIS_ELITE", "CONSTRUCT_MENAGERIE_NORMAL",
        "CORPSE_SLUGS_NORMAL", "CUBEX_CONSTRUCT_NORMAL", "CULTISTS_NORMAL", "DEVOTED_SCULPTOR_WEAK", "ENTOMANCER_ELITE",
        "EXOSKELETONS_NORMAL", "EXOSKELETONS_WEAK", "FAKE_MERCHANT_EVENT_ENCOUNTER", "FLYCONID_NORMAL", "FOSSIL_STALKER_NORMAL",
        "FROG_KNIGHT_NORMAL", "HUNTER_KILLER_NORMAL", "INKLETS_NORMAL", "KAISER_CRAB_BOSS", "LAGAVULIN_MATRIARCH_BOSS",
        "LOUSE_PROGENITOR_NORMAL", "MAWLER_NORMAL", "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER", "NIBBITS_NORMAL", "OVERGROWTH_CRAWLERS",
        "OWL_MAGISTRATE_NORMAL", "PHANTASMAL_GARDENERS_ELITE", "PUNCH_CONSTRUCT_NORMAL", "PUNCH_OFF_EVENT_ENCOUNTER", "RUBY_RAIDERS_NORMAL",
        "SCROLLS_OF_BITING_NORMAL", "SCROLLS_OF_BITING_WEAK", "SEAPUNK_NORMAL", "SEWER_CLAM_NORMAL", "SKULKING_COLONY_ELITE",
        "SLIMED_BERSERKER_NORMAL", "SLIMES_NORMAL", "SLITHERING_STRANGLER_NORMAL", "SLUMBERING_BEETLE_NORMAL", "SNAPPING_JAXFRUIT_NORMAL",
        "SOUL_FYSH_BOSS"
    };

    public static void Test_Encounters1_AllRegistered()
    {
        foreach (var id in Ids)
        {
            Assert.True(EncounterRegistry.TryGet(id, out _), $"{id} is registered");
        }
        Assert.Equal(RoomType.Boss, EncounterRegistry.Get("KAISER_CRAB_BOSS").RoomType, "Kaiser Crab is a boss");
        Assert.Equal(RoomType.Elite, EncounterRegistry.Get("BYRDONIS_ELITE").RoomType, "Byrdonis is an elite");
        Assert.Equal(300, EncounterRegistry.Get("FAKE_MERCHANT_EVENT_ENCOUNTER").MinGoldReward(10), "fake merchant gold ignores Poverty");
        Assert.Equal(7, EncounterRegistry.Get("CULTISTS_NORMAL").MinGoldReward(10), "Poverty scales monster gold 10 x 0.75");
        Assert.True(EncounterRegistry.Get("SLIMES_NORMAL").SharesTagsWith(EncounterRegistry.Get("FLYCONID_NORMAL")), "Slimes tag shared");
    }

    public static void Test_Encounters1_SlimesNormalComposition()
    {
        var enc = EncounterRegistry.Get("SLIMES_NORMAL");
        var spawns = enc.GenerateMonsters(null!, new Rng(12345UL));
        Assert.Equal(4, spawns.Count, "four slimes");
        Assert.Equal(TwigSlimeM.MonsterIdEntry, spawns[0].Def.Id, "twig medium first");
        Assert.Equal(LeafSlimeM.MonsterIdEntry, spawns[1].Def.Id, "leaf medium second");
        Assert.True(spawns[2].Def.Id != spawns[3].Def.Id, "one of each small slime");
    }
}
