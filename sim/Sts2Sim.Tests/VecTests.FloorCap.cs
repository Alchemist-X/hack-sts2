using Sts2Sim.Core;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// The floor stall cap (<c>floor_step_cap</c>): an episode that takes <c>floor_step_cap</c> non-combat decisions on one
/// floor without the floor advancing is truncated with status FLOOR_STALL and end_kind FloorStall — the protocol
/// evaluator's <c>stuck</c> rule (docs/train-smoke-2026-11-02.md: the smoke policy learned a shop
/// BuyEntry → PromptCancel loop that nothing truncated). Combat decisions never count.
/// </summary>
public static unsafe partial class VecTests
{
    /// <summary>A fresh Ironclad run (opening prompt cleared) with 999 gold, put straight into a merchant room.</summary>
    private static RunSim ShopStart(string seed)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, "IRONCLAD", 0) { Content = ContentFilter.PortedOnly });
        ClearPending(sim);
        var rs = sim.State;
        rs.Players[0].Gold = 999;
        RoomStack.EnterRoom(rs, new MerchantRoom());
        sim.Loop.MapOpen = false;
        RunLoop.AfterAction(rs, sim.Loop);
        Assert.Equal(RunPhase.Shop, sim.Phase);
        return sim;
    }

    /// <summary>
    /// The stalling policy: in the shop buy the card-removal service (the last BuyEntry row), at its DeckRemove prompt
    /// cancel. Every decision is legal and non-combat and the floor never advances.
    /// </summary>
    private static int StallAction(NativeBuffers nb, int slot)
    {
        var s = nb.Slot(slot);
        int n = s.CandN;
        var want = s.GlobalI[GI.DecisionKind] == (int)RunPhase.Prompt ? RunActionKind.PromptCancel : RunActionKind.BuyEntry;
        int pick = -1;
        for (int r = 0; r < n; r++)
        {
            if ((RunActionKind)CandRow(nb, slot, r)[CandCol.Kind] == want)
            {
                pick = r;
            }
        }
        Assert.True(pick >= 0, $"no {want} candidate (decision_kind {s.GlobalI[GI.DecisionKind]}, cand_n {n})");
        return pick;
    }

    private static VecEnv FloorCapEnv(int cap, bool autoReset = true)
        => new(new VecConfig(1, Threads: 1, AutoReset: autoReset, FloorStepCap: cap,
            Pool: new FreshPoolSpec(new[] { "IRONCLAD" }, Count: 1, SeedPrefix: "FLOORCAP")));

    /// <summary>
    /// A shop BuyEntry(removal) → PromptCancel loop is truncated at exactly the cap (default 80, and 7), with FLOOR_STALL,
    /// end_kind FloorStall, reward 0, terminated 0, auto-reset into the next episode (or a parked finished slot with
    /// auto_reset off); floor_step_cap = 0 never truncates; illegal indices at a non-combat point count as decisions,
    /// no-ops do not.
    /// </summary>
    public static void Test_VecEnv_FloorStepCapTruncatesShopStall()
    {
        Assert.Equal(80, VecConfig.DefaultFloorStepCap);
        Assert.Equal(80, new VecConfig(1).FloorStepCap, "the default is on");
        Assert.Equal(80, VecConfig.FromJson("{\"num_envs\": 1}").FloorStepCap, "config_json without the key: default 80");
        Assert.Equal(5, VecConfig.FromJson("{\"num_envs\": 1, \"floor_step_cap\": 5}").FloorStepCap);
        Assert.Equal(0, VecConfig.FromJson("{\"num_envs\": 1, \"floor_step_cap\": 0}").FloorStepCap);
        Assert.Throws<ArgumentException>(() => VecConfig.FromJson("{\"num_envs\": 1, \"floor_step_cap\": -1}"));
        Assert.True(Vocab.Enums.Single(e => e.Name == "EndKind").Values.Contains("FloorStall"), "EndKind lists FloorStall");

        var shop = ShopStart("VEC-FLOORCAP");
        int floor = shop.State.TotalFloor;
        var act = new int[1];
        foreach (var (cap, auto) in new[] { (VecConfig.DefaultFloorStepCap, true), (7, true), (7, false) })
        {
            var env = FloorCapEnv(cap, auto);
            using var nb = new NativeBuffers(1);
            env.Restore(0, env.Pool.AddSnapshot(shop.Clone()), nb.Table);
            var s = nb.Slot(0);
            long episode = s.Episode[EpisodeCol.EpisodeId];
            for (int t = 1; t <= cap; t++)
            {
                act[0] = StallAction(nb, 0);
                env.Step(act, nb.Table);
                var st = (SlotStatus)s.Status;
                Assert.Equal(0, (int)(st & (SlotStatus.IllegalAction | SlotStatus.SimError | SlotStatus.ContentGap)), $"cap {cap} step {t}: status {st}");
                if (t < cap)
                {
                    Assert.False(st.HasFlag(SlotStatus.FloorStall), $"cap {cap}: no stall before decision {cap} (step {t})");
                    Assert.Equal(0, (int)s.Truncated, $"cap {cap} step {t}");
                    Assert.Equal(floor, s.GlobalI[GI.TotalFloor], "the loop never advances the floor");
                    Assert.Equal(t, env.Slots[0].FloorSteps);
                    continue;
                }
                Assert.True(st.HasFlag(SlotStatus.FloorStall), $"cap {cap}: decision {cap} truncates (status {st})");
                Assert.Equal(1, (int)s.Truncated);
                Assert.Equal(0, (int)s.Terminated);
                Assert.Equal(0f, s.Reward);
                var le = s.LastEpisode;
                Assert.Equal((int)EndKind.FloorStall, le[LastEpCol.EndKind]);
                Assert.Equal(cap, le[LastEpCol.Steps]);
                Assert.Equal(floor, le[LastEpCol.Floor]);
                Assert.Equal(0, le[LastEpCol.Win]);
                if (auto)
                {
                    Assert.True(st.HasFlag(SlotStatus.Reset), "auto-reset in the same step");
                    Assert.Equal(episode + 1, s.Episode[EpisodeCol.EpisodeId], "episode_id += N (N = 1)");
                    Assert.Equal(0L, s.Episode[EpisodeCol.StepIndex]);
                    Assert.True(s.CandN > 0, "the next episode's candidates are written");
                    Assert.Equal(0, env.Slots[0].FloorSteps, "a new episode starts with an empty stall count");
                }
                else
                {
                    Assert.False(st.HasFlag(SlotStatus.Reset));
                    Assert.Equal(0, s.CandN, "a finished slot lists no candidates");
                    // an odd decision count ends on the removal prompt the last BuyEntry opened
                    Assert.Equal((int)(cap % 2 == 1 ? RunPhase.Prompt : RunPhase.Shop), s.GlobalI[GI.DecisionKind], "the finished slot shows its last state");
                    act[0] = 0;
                    env.Step(act, nb.Table);
                    Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.IllegalAction), "a finished slot rejects actions");
                    Assert.False(((SlotStatus)s.Status).HasFlag(SlotStatus.FloorStall), "and is not truncated again");
                }
            }
        }

        // floor_step_cap = 0: the same loop runs 300 decisions without truncation.
        {
            var env = FloorCapEnv(0);
            using var nb = new NativeBuffers(1);
            env.Restore(0, env.Pool.AddSnapshot(shop.Clone()), nb.Table);
            var s = nb.Slot(0);
            for (int t = 0; t < 300; t++)
            {
                act[0] = StallAction(nb, 0);
                env.Step(act, nb.Table);
                Assert.Equal(0, (int)s.Truncated, $"cap 0 step {t}");
                Assert.False(((SlotStatus)s.Status).HasFlag(SlotStatus.FloorStall));
            }
            Assert.Equal(300L, s.Episode[EpisodeCol.StepIndex]);
        }

        // Illegal indices at a non-combat point are decisions too (the run is otherwise unchanged); no-ops are not.
        {
            var env = FloorCapEnv(6);
            using var nb = new NativeBuffers(1);
            env.Restore(0, env.Pool.AddSnapshot(shop.Clone()), nb.Table);
            var s = nb.Slot(0);
            ulong hash = env.StateHash(0);
            for (int t = 0; t < 20; t++)
            {
                act[0] = -1;
                env.Step(act, nb.Table);
                Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.Noop));
            }
            Assert.Equal(0, env.Slots[0].FloorSteps, "no-ops are not decisions");
            for (int t = 1; t <= 3; t++)
            {
                act[0] = s.CandN + 5;
                env.Step(act, nb.Table);
                Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.IllegalAction));
                Assert.Equal(hash, env.StateHash(0), "an illegal index leaves the run unchanged");
                Assert.Equal(t, env.Slots[0].FloorSteps);
            }
            for (int t = 4; t <= 6; t++)
            {
                act[0] = StallAction(nb, 0);
                env.Step(act, nb.Table);
                Assert.Equal(t == 6 ? 1 : 0, (int)s.Truncated, $"mixed step {t}");
            }
            Assert.True(((SlotStatus)s.Status).HasFlag(SlotStatus.FloorStall));
            Assert.Equal(3, s.LastEpisode[LastEpCol.Steps], "steps counts the applied actions only");

            // an illegal index can itself be the truncating decision
            env.Restore(0, env.Pool.AddSnapshot(shop.Clone()), nb.Table);
            for (int t = 1; t <= 6; t++)
            {
                act[0] = int.MaxValue;
                env.Step(act, nb.Table);
                var st = (SlotStatus)s.Status;
                Assert.True(st.HasFlag(SlotStatus.IllegalAction));
                Assert.Equal(t == 6, st.HasFlag(SlotStatus.FloorStall), $"illegal step {t}: status {st}");
            }
            Assert.Equal((int)EndKind.FloorStall, s.LastEpisode[LastEpCol.EndKind]);
            Assert.Equal(0, s.LastEpisode[LastEpCol.Steps]);
        }
    }

    /// <summary>
    /// Combat decisions never count: a 9,999-HP first combat played end-turn-only for 40 decisions (then play-biased to
    /// the win) under a cap of 3 is never truncated and its stall count stays 0.
    /// </summary>
    public static void Test_VecEnv_FloorStepCapIgnoresCombat()
    {
        var sim = FirstCombat("VEC-FLOORCAP-COMBAT");
        var p = sim.State.Players[0];
        p.MaxHp = 9_999;
        p.CurrentHp = 9_999;
        int floor = sim.State.TotalFloor;
        var env = FloorCapEnv(3);
        using var nb = new NativeBuffers(1);
        env.Restore(0, env.Pool.AddSnapshot(sim.Clone()), nb.Table);
        var s = nb.Slot(0);
        var act = new int[1];
        int combatSteps = 0;
        ulong rng = 4242;
        for (int t = 0; t < 600 && s.GlobalI[GI.IsCombat] == 1; t++)
        {
            act[0] = PlayBiased(nb, 0, ref rng, endTurnOnly: combatSteps < 40);
            env.Step(act, nb.Table);
            combatSteps++;
            Assert.Equal(0, s.Status & (int)(SlotStatus.SimError | SlotStatus.ContentGap), env.Slots[0].LastError ?? "");
            Assert.False(((SlotStatus)s.Status).HasFlag(SlotStatus.FloorStall), $"combat decision {combatSteps} never stalls");
            Assert.Equal(0, (int)s.Truncated);
            Assert.Equal(0, env.Slots[0].FloorSteps, "combat decisions are not counted");
        }
        Assert.True(combatSteps > 40, $"the fight lasted {combatSteps} decisions");
        Assert.Equal(0, s.GlobalI[GI.IsCombat], "the fight was won");
        Assert.Equal(floor, s.GlobalI[GI.TotalFloor], "the rewards screen is on the combat's floor");
    }
}
