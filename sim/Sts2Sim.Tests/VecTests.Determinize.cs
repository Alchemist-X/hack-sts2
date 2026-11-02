using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// Determinized clones (<c>RunSim.CloneDeterminized</c>, <c>CombatSim.CloneDeterminized</c>,
/// <c>sts2vec_clone_determinized</c>): the player view is byte-identical, the hidden state is resampled (draw order,
/// every RNG object, future monster moves), the result is a deterministic function of (state, seed), and pending
/// prompts keep replaying their tape. sim/VEC.md "Determinized clones".
/// </summary>
public static unsafe class VecDeterminizeTests
{
    private static readonly string[] Characters = { "IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER" };

    /// <summary>Uniform-random playouts (PortedOnly, healed below half HP); <paramref name="visit"/> returns false to stop.</summary>
    private static void Playouts(int runs, int maxSteps, int seed, Func<RunSim, bool> visit)
    {
        for (int r = 0; r < runs; r++)
        {
            var rng = new System.Random(seed * 7919 + r);
            var sim = RunSim.NewRun(new RunConfig($"VDET-{seed}-{r}", Characters[r % Characters.Length], r % 3 == 0 ? 10 : 0) { Content = ContentFilter.PortedOnly });
            for (int step = 0; step < maxSteps && !sim.IsTerminal; step++)
            {
                if (!sim.HasPendingChoice && sim.State.Players[0] is { } p && p.CurrentHp > 0 && p.CurrentHp * 2 < p.MaxHp + 1)
                {
                    p.MaxHp = Math.Max(p.MaxHp, 200);
                    p.CurrentHp = p.MaxHp;
                }
                if (!visit(sim))
                {
                    return;
                }
                var legal = sim.LegalActions();
                if (legal.Count == 0)
                {
                    break;
                }
                try
                {
                    sim.Step(legal[rng.Next(legal.Count)]);
                }
                catch (Exception)
                {
                    break;
                }
            }
        }
    }

    private static string? FirstDifference(NativeBuffers a, NativeBuffers b)
    {
        foreach (int k in Buf.Observation)
        {
            var x = a.AllBytes(k);
            var y = b.AllBytes(k);
            if (!x.SequenceEqual(y))
            {
                int at = 0;
                while (at < x.Length && x[at] == y[at])
                {
                    at++;
                }
                return $"{Schema.Buffers[k].Name} (byte {at})";
            }
        }
        return null;
    }

    private static SlotStatus Encode(RunSim sim, NativeBuffers nb)
    {
        var status = SlotStatus.Ok;
        ObservationEncoder.Encode(sim, nb.Slot(0), ref status);
        return status;
    }

    private static bool IsTapeDivergence(Exception e) => e.Message.Contains("tape divergence", StringComparison.Ordinal);

    /// <summary>Steps <paramref name="sim"/> with random legal actions; a tape divergence fails, content gaps end the walk.</summary>
    private static int StepAhead(RunSim sim, System.Random rng, int steps)
    {
        int done = 0;
        for (; done < steps && !sim.IsTerminal; done++)
        {
            var legal = sim.LegalActions();
            if (legal.Count == 0)
            {
                break;
            }
            try
            {
                sim.Step(legal[rng.Next(legal.Count)]);
            }
            catch (Exception e) when (!IsTapeDivergence(e))
            {
                break;
            }
        }
        return done;
    }

    /// <summary>
    /// 500+ sampled decision points (combat, prompts, map / shop / event / reward screens): the encoded observation of a
    /// determinized clone equals the original's byte for byte, the clone's hash differs (its RNG streams were
    /// reseeded), the same seed gives the same clone, and the clone keeps stepping (prompts replay their tape).
    /// </summary>
    public static void Test_VecDeterminize_ObservationIdenticalOn500States()
    {
        using var nb = new NativeBuffers(1);
        using var nb2 = new NativeBuffers(1);
        int combat = 0, prompts = 0, run = 0, visits = 0, stepped = 0, promptResolved = 0;
        var stepRng = new System.Random(5);
        Playouts(60, 1500, 41, sim =>
        {
            if (visits++ % 3 != 0)
            {
                return true;
            }
            ulong seed = (ulong)visits * 0x9E3779B97F4A7C15UL;
            var clone = sim.CloneDeterminized(seed);
            var st = Encode(sim, nb);
            var st2 = Encode(clone, nb2);
            Assert.Equal(st, st2, "status");
            Assert.Null(FirstDifference(nb, nb2), $"the determinized clone shows the same player view (phase {sim.Phase}, prompt {sim.PendingChoice?.SiteId})");
            Assert.NotEqual(sim.StateHash(), clone.StateHash(), "the clone's hidden state was resampled");
            if (visits % 4 == 1)
            {
                Assert.Equal(clone.StateHash(), sim.CloneDeterminized(seed).StateHash(), "same state and seed give the same clone");
                Assert.True(StepAhead(clone.Clone(), stepRng, 25) >= 0);
                stepped++;
            }
            if (sim.HasPendingChoice)
            {
                prompts++;
                // the same answer on the original and on the clone: the clone's re-run (same basis, same tape) must have
                // resampled when it handed out this answer, so the two resolved states differ
                var answer = sim.LegalActions()[0];
                var plain = sim.Clone();
                var hooked = clone.Clone();
                bool resolved = true;
                try
                {
                    plain.Step(answer);
                    hooked.Step(answer);
                }
                catch (Exception e) when (!IsTapeDivergence(e))
                {
                    resolved = false;
                }
                if (resolved)
                {
                    Assert.NotEqual(plain.StateHash(), hooked.StateHash(), "the hooked re-run resampled the hidden state");
                    promptResolved++;
                }
                StepAhead(clone, stepRng, 12); // resolves the prompt through the hooked tape re-run
            }
            else if (sim.State.Combat is { IsInProgress: true })
            {
                combat++;
            }
            else
            {
                run++;
            }
            return combat + prompts + run < 900;
        });
        Console.WriteLine($"     determinized views: {combat} combat, {prompts} prompt, {run} run-screen states identical; {stepped} clones stepped on; {promptResolved} prompt answers resampled");
        Assert.True(combat + prompts + run >= 500, $"only {combat + prompts + run} states");
        Assert.True(combat >= 150 && run >= 100 && prompts >= 30, $"coverage combat {combat} run {run} prompts {prompts}");
        Assert.True(promptResolved >= 20, $"only {promptResolved} prompt answers compared");
    }

    /// <summary>
    /// Completeness: every RNG object reachable from the clone (reflection walk; the shared, construction-only map RNG
    /// excepted) is a different object from, and in a different state than, every RNG object of the original.
    /// </summary>
    public static void Test_VecDeterminize_ReseedsEveryRngObject()
    {
        int states = 0, rngs = 0, visits = 0;
        Func<object, bool> skipMap = o => o is ActMap;
        Playouts(25, 1200, 43, sim =>
        {
            if (visits++ % 9 != 0)
            {
                return true;
            }
            var clone = sim.CloneDeterminized((ulong)visits);
            object origRoot = sim.HasPendingChoice ? sim.State : sim;
            object cloneRoot = sim.HasPendingChoice ? clone.State : clone;
            var before = RngGraphWalker.Collect(origRoot, skipMap);
            var after = RngGraphWalker.Collect(cloneRoot, skipMap);
            Assert.Equal(before.Count, after.Count, "the clone holds as many RNG objects as the original");
            var originals = new HashSet<Rng>(before.Select(b => b.Rng), ReferenceEqualityComparer.Instance);
            var states0 = new HashSet<(ulong, ulong, ulong, ulong)>(before.Select(b => Key(b.Rng)));
            foreach (var (path, rng) in after)
            {
                Assert.False(originals.Contains(rng), $"{path} is shared with the original");
                Assert.False(states0.Contains(Key(rng)), $"{path} kept an original RNG state (not reseeded)");
                rngs++;
            }
            states++;
            return states < 300;
        });
        Console.WriteLine($"     {states} states, {rngs} RNG objects, all reseeded");
        Assert.True(states >= 200, $"only {states} states");
    }

    private static (ulong, ulong, ulong, ulong) Key(Rng r)
    {
        var s = r.ToSnapshot();
        return (s.S0, s.S1, s.S2, s.S3);
    }

    /// <summary>
    /// Two seeds give different draw orders (same multiset as the original) and, for at least one sampled combat, a
    /// different future monster move under the same end-turn policy; the current intents stay as they are.
    /// </summary>
    public static void Test_VecDeterminize_SeedsDifferInDrawOrderAndFutureMoves()
    {
        int drawStates = 0, drawDiffer = 0, moveStates = 0, moveDiffer = 0, visits = 0;
        Playouts(60, 1500, 47, sim =>
        {
            if (visits++ % 5 != 0 || sim.HasPendingChoice || sim.State.Combat is not { IsInProgress: true } cs)
            {
                return true;
            }
            var a = sim.CloneDeterminized(1);
            var b = sim.CloneDeterminized(2);
            var draw = cs.Player.DrawPile.Cards;
            if (draw.Select(c => c.Def.Id).Distinct().Count() >= 2 && draw.Count >= 5)
            {
                var da = a.State.Combat!.Player.DrawPile.Cards.Select(c => c.InstanceId).ToList();
                var db = b.State.Combat!.Player.DrawPile.Cards.Select(c => c.InstanceId).ToList();
                Assert.SequenceEqual(draw.Select(c => c.InstanceId).OrderBy(x => x).ToList(), da.OrderBy(x => x).ToList(), "seed 1 keeps the draw multiset");
                Assert.SequenceEqual(da.OrderBy(x => x).ToList(), db.OrderBy(x => x).ToList(), "seed 2 keeps the draw multiset");
                drawStates++;
                var ma = a.State.Combat!.Player.DrawPile.Cards.Select(c => c.Def.Id).ToList();
                var mb = b.State.Combat!.Player.DrawPile.Cards.Select(c => c.Def.Id).ToList();
                drawDiffer += ma.SequenceEqual(mb) ? 0 : 1;
            }
            Assert.SequenceEqual(Intents(sim), Intents(a), "the current intents stay (seed 1)");
            Assert.SequenceEqual(Intents(sim), Intents(b), "the current intents stay (seed 2)");
            if (moveStates < 150)
            {
                moveStates++;
                moveDiffer += FutureMoves(a).SequenceEqual(FutureMoves(b)) ? 0 : 1;
            }
            return drawStates < 300 || moveStates < 150;
        });
        Console.WriteLine($"     draw order differs in {drawDiffer}/{drawStates} states; future monster moves differ in {moveDiffer}/{moveStates}");
        Assert.True(drawStates >= 100, $"only {drawStates} draw states");
        Assert.True(drawDiffer * 10 >= drawStates * 9, $"draw order differs in only {drawDiffer}/{drawStates}");
        Assert.True(moveDiffer >= 1, "at least one future monster move differs between seeds");
    }

    private static List<string> Intents(RunSim sim) =>
        sim.State.Combat!.Monsters.Select(m => $"{m.CombatId}:{(m.HasAi ? m.Ai.NextMove?.Id : null)}").ToList();

    /// <summary>The monsters' next-move ids after each of up to 4 end turns (first legal action at prompts and when EndTurn is not offered).</summary>
    private static List<string> FutureMoves(RunSim sim)
    {
        var log = new List<string>();
        int ends = 0;
        for (int step = 0; step < 60 && ends < 4 && !sim.IsTerminal && sim.State.Combat is { IsInProgress: true }; step++)
        {
            var legal = sim.LegalActions();
            if (legal.Count == 0)
            {
                break;
            }
            int pick = 0;
            for (int i = 0; i < legal.Count; i++)
            {
                if (legal[i].Kind == RunActionKind.EndTurn)
                {
                    pick = i;
                    ends++;
                    break;
                }
            }
            try
            {
                sim.Step(legal[pick]);
            }
            catch (Exception e) when (!IsTapeDivergence(e))
            {
                break;
            }
            if (sim.State.Combat is { } cs)
            {
                log.Add(string.Join(",", cs.Monsters.Select(m => $"{m.CombatId}:{(m.HasAi ? m.Ai.NextMove?.Id : null)}")));
            }
        }
        return log;
    }

    /// <summary>CombatSim.CloneDeterminized: multiset kept, order resampled, deterministic, steppable through prompts.</summary>
    public static void Test_VecDeterminize_CombatSim()
    {
        int checkedStates = 0, differ = 0;
        var rng = new System.Random(3);
        foreach (var (encounter, seed) in new[] { ("NIBBITS_WEAK", 21UL), ("INKLETS_NORMAL", 22UL), ("FROG_KNIGHT_NORMAL", 23UL), ("NIBBITS_WEAK", 24UL) })
        {
            var sim = CombatSim.Create(seed, StartingDecks.Ironclad, encounter);
            for (int step = 0; step < 80 && !sim.IsTerminal; step++)
            {
                var a = sim.CloneDeterminized(7);
                var b = sim.CloneDeterminized(8);
                Assert.Equal(a.StateHash(), sim.CloneDeterminized(7).StateHash(), "deterministic");
                Assert.NotEqual(sim.StateHash(), a.StateHash(), "resampled");
                var orig = sim.State.Player.DrawPile.Cards.Select(c => c.InstanceId).OrderBy(x => x).ToList();
                Assert.SequenceEqual(orig, a.State.Player.DrawPile.Cards.Select(c => c.InstanceId).OrderBy(x => x).ToList(), "multiset");
                if (sim.State.Player.DrawPile.Count >= 4)
                {
                    differ += a.State.Player.DrawPile.Cards.Select(c => c.InstanceId).SequenceEqual(b.State.Player.DrawPile.Cards.Select(c => c.InstanceId)) ? 0 : 1;
                    checkedStates++;
                }
                var legal = b.LegalActions();
                if (legal.Count > 0)
                {
                    b.Step(legal[rng.Next(legal.Count)]);
                }
                legal = sim.LegalActions();
                if (legal.Count == 0)
                {
                    break;
                }
                sim.Step(legal[rng.Next(legal.Count)]);
            }
        }
        Assert.True(checkedStates >= 20 && differ * 10 >= checkedStates * 8, $"draw order differs in {differ}/{checkedStates}");
    }

    /// <summary>
    /// sts2vec_clone_determinized semantics through VecEnv: the entry restores into another slot with the same
    /// observation bytes as the source slot, a different hash, and the same hash on every restore.
    /// </summary>
    public static void Test_VecDeterminize_EnvCloneDeterminizedRestores()
    {
        var env = new VecEnv(new VecConfig(2, Threads: 1, Pool: new FreshPoolSpec(new[] { "SILENT" }, Count: 1, SeedPrefix: "VDET")));
        using var nb = new NativeBuffers(2);
        env.Reset(default, default, nb.Table);
        var rng = new System.Random(9);
        int tested = 0;
        for (int t = 0; t < 400 && tested < 25; t++)
        {
            int n = nb.Slot(0).CandN;
            if (n == 0)
            {
                break;
            }
            env.Step(new[] { rng.Next(n), -1 }, nb.Table);
            if (t % 9 != 0)
            {
                continue;
            }
            int entry = env.CloneDeterminized(0, (ulong)t + 1);
            env.Restore(1, entry, nb.Table);
            foreach (int k in Buf.Observation)
            {
                var all = nb.AllBytes(k);
                int half = all.Length / 2;
                Assert.True(all[..half].SequenceEqual(all[half..]), $"{Schema.Buffers[k].Name}: slot 1 (restored clone) shows slot 0's view");
            }
            ulong h1 = env.StateHash(1);
            Assert.NotEqual(env.StateHash(0), h1, "hidden state resampled");
            env.Restore(1, entry, nb.Table);
            Assert.Equal(h1, env.StateHash(1), "restoring the same entry twice gives the same state");
            tested++;
        }
        Assert.True(tested >= 20, $"only {tested} clones");
    }
}
