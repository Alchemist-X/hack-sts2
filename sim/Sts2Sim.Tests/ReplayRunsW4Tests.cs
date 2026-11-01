using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.State;
using System.Text.Json.Nodes;
using Sts2Sim.Diff.V2;

namespace Sts2Sim.Tests;

public static class ReplayRunsW4Tests
{
    private static void ChooseNeowOption(RunSim sim, string relicId)
    {
        var ev = Assert.NotNull(RunLegalActionGenerator.CurrentEvent(sim.State));
        int index = ev.CurrentOptions.ToList().FindIndex(o => o.TextKey.EndsWith("." + relicId, StringComparison.Ordinal));
        Assert.True(index >= 0, $"Neow offers {relicId}");
        sim.Step(RunAction.Of(RunActionKind.ChooseEventOption, index));
    }

    private static int RewardIndex(RunSim sim, Func<Reward, bool> match)
    {
        var set = Assert.NotNull(RunLegalActionGenerator.TopRewards(sim.State));
        for (int i = 0; i < set.Rewards.Count; i++)
        {
            if (!set.Rewards[i].SuccessfullySelected && match(set.Rewards[i]))
            {
                return i;
            }
        }
        Assert.Fail("no matching unclaimed reward");
        return -1;
    }

    public static void Test_ReplayRuns_NeowsBonesRollsItsCurseAfterTheRewardsScreen()
    {
        var sim = RunSim.NewRun(new RunConfig("G0G2JMFEYHRS", StartingDecks.IroncladCharacterId, 10));
        ChooseNeowOption(sim, NeowsBones.RelicId);
        Assert.Equal(RunPhase.Rewards, sim.Phase);
        var player = sim.State.Players[0];
        Assert.False(player.Deck.Any(c => c.Id == "GUILTY"), "the curse waits for the rewards screen (Models.Relics/NeowsBones.cs:82 awaits Offer before :118-128)");
        sim.Step(RunAction.Of(RunActionKind.ClaimReward, RewardIndex(sim, r => r is RelicReward rr && rr.RelicId == "LEAFY_POULTICE")));
        sim.Step(RunAction.Of(RunActionKind.ClaimReward, RewardIndex(sim, r => r is RelicReward rr && rr.RelicId == Kaleidoscope.RelicId)));
        var kaleidoscope = Assert.NotNull(RunLegalActionGenerator.TopRewards(sim.State));
        var offered = kaleidoscope.Rewards.OfType<CardReward>().SelectMany(r => r.Cards.Select(c => c.Card.Id)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.SequenceEqual(new[] { "ARSENAL", "GLACIER", "LIGHTNING_ROD", "RESONANCE", "SEVERANCE", "UP_MY_SLEEVE" }, offered, "recorded Kaleidoscope offers of run G0G2JMFEYHRS (floor 1 card_choices)");
        for (int guard = 0; guard < 4 && sim.Phase == RunPhase.Rewards; guard++)
        {
            sim.Step(RunAction.Of(RunActionKind.ProceedRewards));
        }
        Assert.Equal(RunPhase.Event, sim.Phase);
        Assert.Equal(1, player.Deck.Count(c => c.Id == "GUILTY"), "the recorded run's first combat deck holds GUILTY");
        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
    }

    public static void Test_ReplayRuns_CombatRoomRecordsEncounterMonstersInHistory()
    {
        var sim = RunSim.NewRun(new RunConfig("GM28D28DX794", "SILENT", 6));
        ChooseNeowOption(sim, "DOWSING_ROD");
        for (int guard = 0; guard < 6 && sim.Phase != RunPhase.Map; guard++)
        {
            var leave = sim.LegalActions().First(a => a.Kind is RunActionKind.LeaveEvent or RunActionKind.ProceedRewards);
            sim.Step(leave);
        }
        Assert.Equal(RunPhase.Map, sim.Phase);
        var legal = new List<MapPoint>();
        RunLegalActionGenerator.GetLegalMapPoints(sim.State, legal);
        int index = legal.FindIndex(p => p.coord.col == 4 && p.coord.row == 1);
        Assert.True(index >= 0, "recorded first move (4,1) is legal");
        var before = sim.Clone();
        sim.Step(RunAction.Of(RunActionKind.Travel, index));
        Assert.Equal(RunPhase.Combat, sim.Phase);
        var room = sim.State.MapPointHistory[^1].Rooms[^1];
        Assert.Equal("CORPSE_SLUGS_WEAK", room.ModelId);
        Assert.SequenceEqual(new[] { "CORPSE_SLUG", "CORPSE_SLUG" }, room.MonsterIds, "Rooms/CombatRoom.cs:381-398 adds every encounter monster without dedupe (recorded floor 2 summary)");
        Assert.Equal(0, before.State.MapPointHistory.Sum(e => e.Rooms.Sum(r => r.MonsterIds.Count)), "the clone taken before the room is untouched");
    }

    public static void Test_ReplayRuns_G3PrepareDropsSupersededAttemptsAndSeedsTheFirstSegment()
    {
        var bundle = JsonNode.Parse("""
        {"bundle_version":2,"kind":"run","game_mode":"custom","modifiers":[],"anchors":[{"id":"a0","kind":"mcr_initial"}],
         "segments":[
          {"id":"s0","kind":"room","act":1,"floor":1,"room_type":"Event","room_model_id":"EVENT.NEOW","attempt":1,"start":{"player":{}},"decisions":[]},
          {"id":"s1","kind":"combat","act":1,"floor":2,"room_type":"Monster","room_model_id":"ENCOUNTER.X","attempt":1,"start":{"deck":[]},"decisions":[],"outcome":{"result":"abandoned","final_hp":3}},
          {"id":"s2","kind":"combat","act":1,"floor":2,"room_type":"Monster","room_model_id":"ENCOUNTER.X","attempt":2,"start_anchor":"a0","decisions":[],"outcome":{"result":"win","final_hp":5}},
          {"id":"s3","kind":"room","phase":"post_combat","act":1,"floor":2,"room_type":"Monster","room_model_id":"ENCOUNTER.X","attempt":2,"decisions":[
             {"index":0,"screen":"rewards","pre_state":null,"action":{"type":"reward_take","reward_type":"gold_reward"}}]},
          {"id":"s4","kind":"room","phase":"post_combat_resume","act":1,"floor":2,"room_type":"Monster","room_model_id":"ENCOUNTER.X","attempt":2,"decisions":[
             {"index":0,"screen":"rewards","pre_state":null,"action":{"type":"reward_take","reward_type":"gold_reward"}},
             {"index":1,"screen":"rewards","pre_state":null}]}
         ]}
        """)!.AsObject();
        var prepared = G3Replay.Prepare(bundle);
        var segs = prepared.Bundle["segments"]!.AsArray();
        Assert.SequenceEqual(new[] { "s0", "s2", "s4" }, segs.Select(s => s!["id"]!.GetValue<string>()));
        Assert.SequenceEqual(new[] { "s1", "s3" }, prepared.DroppedAttempts);
        Assert.Equal("seed", segs[0]!["start"]!["from"]!.GetValue<string>());
        Assert.True(segs[1]!["start_anchor"] == null && segs[1]!["start"] == null, "later segments chain");
        Assert.True(segs[1]!["outcome"]!["final_hp"] == null, "final_hp is recorded before end-of-combat heals and is dropped");
        Assert.Equal("standard", prepared.Bundle["game_mode"]!.GetValue<string>());
        Assert.Equal("gold", segs[2]!["decisions"]![0]!["action"]!["reward_type"]!.GetValue<string>());
        Assert.Equal(1, segs[2]!["decisions"]!.AsArray().Count, "truncated at the unresolved decision");
        Assert.Equal("s4#1", prepared.TruncatedAt);
    }
}
