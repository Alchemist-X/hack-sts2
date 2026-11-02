using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.Unlocks;

namespace Sts2Sim.Tests;

public static class Content_characters_actsTests
{
    private static readonly string[] CharacterIds = { "DEFECT", "IRONCLAD", "NECROBINDER", "REGENT", "SILENT" };

    private static readonly string[] ActIds = { "GLORY", "HIVE", "OVERGROWTH", "UNDERDOCKS" };

    public static void Test_CharactersActs_AllPortedIdsRegistered()
    {
        foreach (var id in CharacterIds)
        {
            Assert.True(CharacterRegistry.TryGet(id, out _), $"character {id} registered");
        }
        foreach (var id in ActIds)
        {
            Assert.True(ActRegistry.TryGet(id, out _), $"act {id} registered");
        }
    }

    public static void Test_CharactersActs_StartingDataResolves()
    {
        foreach (var id in CharacterIds)
        {
            var def = CharacterRegistry.Get(id);
            foreach (var card in def.StartingDeck)
            {
                Assert.True(CardRegistry.Defs.Contains(card), $"{id} starting card {card} registered");
            }
            foreach (var relic in def.StartingRelics)
            {
                Assert.True(RelicRegistry.Models.Contains(relic), $"{id} starting relic {relic} registered");
            }
            var start = RunLoop.GetCharacterStart(id);
            Assert.Equal(def.StartingHp, start.StartingHp);
            Assert.Equal(99, start.StartingGold);
        }
        Assert.Equal(3, RunLoop.GetCharacterStart("DEFECT").BaseOrbSlotCount);
        Assert.Equal(12, CharacterRegistry.Get("SILENT").StartingDeck.Count);
        Assert.Equal(66, CharacterRegistry.Get("NECROBINDER").StartingHp);
    }

    public static void Test_CharactersActs_ActsByIndexAndFirstRunOrder()
    {
        var byIndex = ActModel.ActsByIndex;
        Assert.Equal(3, byIndex.Count);
        Assert.SequenceEqual(new[] { "OVERGROWTH", "UNDERDOCKS" }, byIndex[0].Select(a => a.Id));
        Assert.SequenceEqual(new[] { "HIVE" }, byIndex[1].Select(a => a.Id));
        Assert.SequenceEqual(new[] { "GLORY" }, byIndex[2].Select(a => a.Id));
        Assert.False(ActRegistry.Get("UNDERDOCKS").IsUnlocked(UnlockProfile.None));

        var act = new ActInstance(ActRegistry.Get("OVERGROWTH"));
        act.GenerateRooms(new RunRngSet("CHARACTERS_ACTS").UpFront, UnlockProfile.None);
        act.ApplyDiscoveryOrderModifications(UnlockProfile.None);
        Assert.Equal(15, act.Rooms.normalEncounters.Count);
        Assert.SequenceEqual(new[] { "NIBBITS_WEAK", "SLIMES_WEAK", "SHRINKER_BEETLE_WEAK", "INKLETS_NORMAL", "MAWLER_NORMAL", "RUBY_RAIDERS_NORMAL", "NIBBITS_NORMAL" },
            act.Rooms.normalEncounters.Take(7).Select(e => e.Id));
        Assert.SequenceEqual(new[] { "BYRDONIS_NEST", "SAPPHIRE_SEED" }, act.Rooms.events.Take(2));
        Assert.SequenceEqual(new[] { "BYRDONIS_ELITE", "PHROG_PARASITE_ELITE" }, act.Rooms.eliteEncounters.Take(2).Select(e => e.Id));
        Assert.Equal("VANTOM_BOSS", act.BossEncounter.Id);
    }
}
