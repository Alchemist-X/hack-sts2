using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Core.Unlocks;
using Sts2Sim.Diff;

namespace Sts2Sim.Tests;

public static class SelfConsistencyTests
{
    private const int WalkStepCap = 400;

    private const int WalkMaxHp = 600;

    private static readonly int[] WalkSeeds = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

    private static readonly UnlockProfile NoNeowUnlocks = new(
        UnlockProfile.AllEpochIds.Where(e => !string.Equals(e, RunLoop.NeowEpochId, StringComparison.Ordinal)),
        UnlockProfile.AllEncounterIds,
        UnlockProfile.AllNumberOfRuns);

    private static readonly (string Encounter, ulong Seed)[] CombatStreams =
    {
        ("NIBBITS_WEAK", 11UL),
        ("NIBBITS_WEAK", 12UL),
        ("INKLETS_NORMAL", 13UL),
        ("FROG_KNIGHT_NORMAL", 14UL)
    };

    private const string PinnedCombatStreams = "NIBBITS_WEAK/11:18:D954CBBD6B692983 NIBBITS_WEAK/12:22:ACFD58206F8E0BD8 INKLETS_NORMAL/13:18:89248C839411E7CD FROG_KNIGHT_NORMAL/14:19:DB175B3A2B7CA7D5";

    private const string PinnedRunStreams = "1:136:6A45E2AEAB5007FC 2:114:E04B6771D3A9FB20 3:160:83A2572D6A5DD790 4:400:52982AD74C03D66B";

    public static void Test_SelfConsistency_F2_CombatHashStreamsArePinnedAndRepeatable()
    {
        var first = CombatStreams.Select(s => CombatStream(s.Encounter, s.Seed)).ToArray();
        var second = CombatStreams.Select(s => CombatStream(s.Encounter, s.Seed)).ToArray();
        for (int i = 0; i < first.Length; i++)
        {
            Assert.SequenceEqual(first[i].Hashes, second[i].Hashes, $"{CombatStreams[i]}: two in-process runs give the same hash stream");
        }
        string actual = string.Join(" ", CombatStreams.Select((s, i) => $"{s.Encounter}/{s.Seed}:{first[i].Hashes.Count}:{Digest(first[i].Hashes):X16}"));
        Assert.Equal(PinnedCombatStreams, actual, "combat hash streams (pinned from Release; Debug must match)");
    }

    public static void Test_SelfConsistency_F2_RunHashStreamsArePinnedAndRepeatable()
    {
        var seeds = WalkSeeds.Take(4).ToArray();
        var first = seeds.Select(s => Walk(NoNeowConfig(s), s, WalkStepCap, avoidEvents: true)).ToArray();
        var second = seeds.Select(s => Walk(NoNeowConfig(s), s, WalkStepCap, avoidEvents: true)).ToArray();
        for (int i = 0; i < first.Length; i++)
        {
            Assert.SequenceEqual(first[i].Actions, second[i].Actions, $"run {seeds[i]}: the same seed and policy pick the same actions");
            Assert.SequenceEqual(first[i].Hashes, second[i].Hashes, $"run {seeds[i]}: two in-process runs give the same hash stream");
        }
        string actual = string.Join(" ", seeds.Select((s, i) => $"{s}:{first[i].Hashes.Count}:{Digest(first[i].Hashes):X16}"));
        Assert.Equal(PinnedRunStreams, actual, "run hash streams (pinned from Release; Debug must match)");
    }

    public static void Test_SelfConsistency_F3_FixtureExportsBackToItself()
    {
        var save = RoundTripFixture();
        var reasons = RunImporter.CheckSupported(save, ImportOptions.Default);
        Assert.Equal(0, reasons.Count, "the edited fixture is importable: " + string.Join("; ", reasons));
        var imported = RunImporter.ImportState(save, ImportOptions.Default);
        var exported = RunExporter.ToSave(imported);
        var expected = RunExporter.Describe(save).Where(l => !IsIgnored(l)).ToList();
        var actual = RunExporter.Describe(exported).Where(l => !IsIgnored(l)).ToList();
        AssertSameLines(expected, actual, "ToSave(FromSave(fixture))");
        var again = RunImporter.ImportState(exported, ImportOptions.Default);
        Assert.Equal(StateHasher.Hash(imported.State), StateHasher.Hash(again.State), "FromSave(ToSave(x)) hashes equal to x for the imported fixture");
        Assert.Equal(imported.EntryPoint, again.EntryPoint);
    }

    public static void Test_SelfConsistency_F3_ExportImportIsIdempotentOnWalkedRuns()
    {
        int checkedStates = 0;
        foreach (var seed in WalkSeeds)
        {
            Walk(NoNeowConfig(seed), seed, WalkStepCap, avoidEvents: true, before: (sim, step) =>
            {
                if (step % 3 != 0 || sim.HasPendingChoice || sim.State.Combat != null || sim.State.Map == null)
                {
                    return;
                }
                string label = $"run {seed} step {step} ({sim.Phase})";
                var s1 = RunExporter.ToSave(sim);
                var x = RunImporter.ImportState(s1, ImportOptions.Default);
                var s2 = RunExporter.ToSave(x);
                AssertSameLines(RunExporter.Describe(s1).ToList(), RunExporter.Describe(s2).ToList(), label + ": ToSave(FromSave(ToSave(x)))");
                var y = RunImporter.ImportState(s2, ImportOptions.Default);
                Assert.Equal(StateHasher.Hash(x.State), StateHasher.Hash(y.State), label + ": FromSave(ToSave(FromSave(s))) hashes equal");
                var player = sim.State.Players[0];
                var loaded = x.State.Players[0];
                Assert.Equal((player.CurrentHp, player.MaxHp, player.Gold, player.Deck.Count, player.Relics.Count), (loaded.CurrentHp, loaded.MaxHp, loaded.Gold, loaded.Deck.Count, loaded.Relics.Count), label + ": player survives the round trip");
                Assert.Equal(StateHasher.Hash(sim.State.Rng), StateHasher.Hash(x.State.Rng), label + ": run RNG survives the round trip");
                checkedStates++;
            });
        }
        Assert.True(checkedStates > 0, "the walks reached exportable states");
        Console.WriteLine($"     SelfConsistency F3: {checkedStates} walked run states exported, re-imported and re-exported");
    }

    public static void Test_SelfConsistency_F4_RunLegalActionClosure()
    {
        var counts = new ClosureCounts();
        foreach (var seed in WalkSeeds)
        {
            var walk = Walk(NoNeowConfig(seed), seed, WalkStepCap, avoidEvents: true, before: (sim, step) => CheckClosure(sim, $"run {seed} step {step}", step, counts));
            CheckTerminal(walk.Sim, $"run {seed} end", counts);
            counts.Note(walk);
        }
        Console.WriteLine($"     SelfConsistency F4: {counts.States} run states, {counts.Prompts} prompt states, {counts.Stepped} listed actions stepped on clones, {counts.Candidates} candidates checked with IsLegal, {counts.Illegal} illegal actions stepped (all threw, hash unchanged), {counts.Terminal} terminal states with no action, {counts.SkippedEventTravel} listed travels to event points not stepped (unported events under PortedOnly); {counts.Describe()}");
    }

    public static void Test_SelfConsistency_F4F5_NewRunWithNeowAndEvents()
    {
        var counts = new ClosureCounts();
        long checks = 0;
        foreach (var seed in WalkSeeds.Take(4))
        {
            var ledger = new RunLedger();
            var walk = Walk(DefaultConfig(seed), seed, WalkStepCap, avoidEvents: false,
                before: (sim, step) => CheckClosure(sim, $"neow run {seed} step {step}", step, counts, avoidEvents: false),
                after: (sim, step) => ledger.Check(sim, $"neow run {seed} after step {step}"),
                rethrowGaps: true);
            CheckTerminal(walk.Sim, $"neow run {seed} end", counts);
            counts.Note(walk);
            checks += ledger.Checks;
        }
        Console.WriteLine($"     SelfConsistency F4/F5 (Neow, events): {counts.States} states, {counts.Stepped} actions stepped on clones, {checks} invariant checks; {counts.Describe()}");
    }

    public static void Test_SelfConsistency_F5_RunInvariantsHoldAfterEveryStep()
    {
        var counts = new ClosureCounts();
        long checks = 0;
        foreach (var seed in WalkSeeds)
        {
            var ledger = new RunLedger();
            var walk = Walk(NoNeowConfig(seed), seed, WalkStepCap, avoidEvents: true,
                start: sim => ledger.Check(sim, $"run {seed} at start"),
                after: (sim, step) => ledger.Check(sim, $"run {seed} after step {step}"));
            counts.Note(walk);
            checks += ledger.Checks;
        }
        Console.WriteLine($"     SelfConsistency F5: {checks} run-scope invariant checks (HP, block, energy, stars, gold, potion slots, owners, card conservation across piles and deck, creature ids, power owners, run/player RNG counters, card id allocator); {counts.Describe()}");
    }

    public static void Test_SelfConsistency_F5_LedgerRejectsBrokenRunStates()
    {
        var sim = RunSim.NewRun(NoNeowConfig(1));
        var ledger = new RunLedger();
        ledger.Check(sim, "fresh run");
        var broken = sim.Clone();
        broken.State.Players[0].Gold = -1;
        Assert.Throws<AssertionException>(() => new RunLedger().Check(broken, "negative gold"));
        broken = sim.Clone();
        broken.State.Players[0].Deck.Add(broken.State.Players[0].Deck[0]);
        Assert.Throws<AssertionException>(() => new RunLedger().Check(broken, "duplicated deck card"));
        broken = sim.Clone();
        broken.State.Rng.UpFront.NextInt(2);
        var forward = new RunLedger();
        forward.Check(broken, "advanced rng");
        Assert.Throws<AssertionException>(() => forward.Check(sim, "rewound rng"));
    }

    public static void Test_SelfConsistency_F7_ReplayFromCloneMatchesOriginal()
    {
        int replays = 0;
        foreach (var seed in WalkSeeds)
        {
            var clones = new List<(int Step, RunSim Clone)>();
            var walk = Walk(NoNeowConfig(seed), seed, WalkStepCap, avoidEvents: true, before: (sim, step) =>
            {
                if (step % 17 == 5)
                {
                    clones.Add((step, sim.Clone()));
                }
            });
            foreach (var (step, clone) in clones)
            {
                for (int i = step; i < walk.Actions.Count; i++)
                {
                    clone.Step(walk.Actions[i]);
                    Assert.Equal(walk.Hashes[i], clone.StateHash(), $"run {seed}: replay from the step-{step} clone diverges at step {i} ({walk.Actions[i]})");
                }
                replays++;
            }
        }
        Assert.True(replays > 0, "the walks were long enough to replay");
        Console.WriteLine($"     SelfConsistency F7 replay: {replays} clone replays matched the original hash stream");
    }

    /// <summary>
    /// Clone fidelity as a property (CONTRACTS.md Part A §6): at ≥ 500 random decision points of seeded random runs (every
    /// character, A0 and A10, boosted HP, ported content) a clone is taken and stepped in lockstep with the original for 1–4
    /// actions; the clone hashes like its source when taken and like the original after every action. On Rewards screens the
    /// policy claims potion rewards and then uses or discards belt potions, and every such Rewards-phase potion action is a
    /// clone point (the claimed PotionReward / belt aliasing that clone-then-step used to break, E2c ticket 1).
    /// </summary>
    public static void Test_SelfConsistency_F7_CloneThenStepMatchesStep()
    {
        const int MinPoints = 500, MinRewardPotionPoints = 40, MaxRuns = 120, RunStepCap = 900;
        var characters = CharacterRegistry.AllIds;
        var legal = new List<RunAction>(64);
        var choices = new List<RunAction>(64);
        var live = new List<(RunSim Clone, int Left, string Label)>();
        var rewardPotionKinds = new SortedDictionary<RunActionKind, int>();
        var phases = new SortedDictionary<RunPhase, int>();
        int runs = 0, points = 0, rewardPotionPoints = 0, lockstep = 0, gaps = 0;
        for (int r = 0; r < MaxRuns && (points < MinPoints || rewardPotionPoints < MinRewardPotionPoints); r++)
        {
            runs++;
            string character = characters[r % characters.Count];
            int ascension = r / characters.Count % 2 == 0 ? 0 : 10;
            var sim = RunSim.NewRun(new RunConfig($"CLONESTEP-{r}", character, ascension) { Content = ContentFilter.PortedOnly });
            var random = new System.Random(7919 * r + 13);
            bool potionClaimed = false;
            live.Clear();
            for (int step = 0; step < RunStepCap && !sim.IsTerminal; step++)
            {
                var player = sim.State.Players[0];
                if (live.Count == 0 && !sim.HasPendingChoice && player.CurrentHp > 0 && player.CurrentHp * 2 <= player.MaxHp)
                {
                    // state edits outside a step only while no clone is in lockstep (the clones would not see them)
                    player.MaxHp = Math.Max(player.MaxHp, 400);
                    player.CurrentHp = player.MaxHp;
                }
                if (sim.State.RewardsStack.Count == 0)
                {
                    potionClaimed = false;
                }
                sim.LegalActions(legal);
                var phase = sim.Phase;
                bool rewardPotion = false;
                RunAction chosen;
                if (phase == RunPhase.Rewards)
                {
                    var set = RunLegalActionGenerator.TopRewards(sim.State)!;
                    int claimPotion = legal.FindIndex(a => a.Kind == RunActionKind.ClaimReward && set.Rewards[a.A] is PotionReward);
                    choices.Clear();
                    choices.AddRange(legal.Where(a => a.Kind is RunActionKind.UsePotion or RunActionKind.DiscardPotion));
                    if (claimPotion >= 0 && player.HasOpenPotionSlot && random.Next(10) < 9)
                    {
                        chosen = legal[claimPotion];
                        potionClaimed = true;
                    }
                    else if (choices.Count > 0 && (potionClaimed || claimPotion >= 0) && random.Next(10) < 6)
                    {
                        chosen = choices[random.Next(choices.Count)];
                        rewardPotion = true;
                    }
                    else
                    {
                        choices.Clear();
                        choices.AddRange(legal.Where(a => a.Kind == RunActionKind.ClaimReward));
                        chosen = choices.Count > 0 && random.Next(10) < 8 ? choices[random.Next(choices.Count)] : legal[random.Next(legal.Count)];
                    }
                }
                else if (phase == RunPhase.Combat && !sim.HasPendingChoice && random.Next(8) != 0 && legal.Any(a => a.Kind == RunActionKind.PlayCard))
                {
                    choices.Clear();
                    choices.AddRange(legal.Where(a => a.Kind == RunActionKind.PlayCard));
                    chosen = choices[random.Next(choices.Count)];
                }
                else
                {
                    choices.Clear();
                    choices.AddRange(legal.Where(a => a.Kind != RunActionKind.DiscardPotion));
                    chosen = choices.Count > 0 ? choices[random.Next(choices.Count)] : legal[random.Next(legal.Count)];
                }

                if (rewardPotion || random.Next(100) < 12)
                {
                    string label = $"run {r} ({character} A{ascension}) step {step} {phase} {chosen}";
                    var clone = sim.Clone();
                    Assert.Equal(sim.StateHash(), clone.StateHash(), label + ": Hash(x) == Hash(x.Clone())");
                    live.Add((clone, 1 + random.Next(4), label));
                    points++;
                    phases[phase] = phases.GetValueOrDefault(phase) + 1;
                    if (rewardPotion)
                    {
                        rewardPotionPoints++;
                        rewardPotionKinds[chosen.Kind] = rewardPotionKinds.GetValueOrDefault(chosen.Kind) + 1;
                    }
                }
                try
                {
                    sim.Step(chosen);
                }
                catch (NotImplementedException)
                {
                    gaps++;
                    break; // a content gap on this seed ends the run (RunSim.Step restored the state; the clones are dropped)
                }
                if (live.Count == 0)
                {
                    continue;
                }
                ulong hash = sim.StateHash();
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    var (clone, left, label) = live[i];
                    clone.Step(chosen);
                    lockstep++;
                    if (clone.StateHash() != hash)
                    {
                        Assert.Fail($"{label}: the clone diverges from the original after {chosen}; {Divergence(sim, clone)}");
                    }
                    if (left <= 1)
                    {
                        live.RemoveAt(i);
                    }
                    else
                    {
                        live[i] = (clone, left - 1, label);
                    }
                }
            }
        }
        Console.WriteLine($"     SelfConsistency F7 clone-then-step: {runs} runs, {points} clone points ({string.Join(", ", phases.Select(p => $"{p.Key} {p.Value}"))}), "
                          + $"{lockstep} lockstep steps with equal hashes; Rewards-phase potion actions {rewardPotionPoints} ({string.Join(", ", rewardPotionKinds.Select(p => $"{p.Key} {p.Value}"))}); {gaps} runs ended on a content gap");
        Assert.True(points >= MinPoints, $"only {points} clone points in {runs} runs");
        Assert.True(rewardPotionPoints >= MinRewardPotionPoints, $"only {rewardPotionPoints} Rewards-phase potion actions in {runs} runs");
    }

    /// <summary>The top-level parts (loop, players, RNG, rooms, each reward on the stack, combat) whose canonical bytes differ.</summary>
    private static string Divergence(RunSim a, RunSim b)
    {
        var parts = new List<string>();
        void Compare(string name, ICanonical? x, ICanonical? y)
        {
            if (x == null || y == null ? x != y : StateHasher.Hash(x) != StateHasher.Hash(y))
            {
                parts.Add(name);
            }
        }

        var (sa, sb) = (a.State, b.State);
        if (a.HasPendingChoice || b.HasPendingChoice)
        {
            parts.Add("(a prompt is pending: the parts below are the live states, not the hashed basis)");
        }
        Compare("loop", a.Loop, b.Loop);
        for (int i = 0; i < Math.Min(sa.Players.Count, sb.Players.Count); i++)
        {
            Compare($"player {i}", sa.Players[i], sb.Players[i]);
        }
        Compare("run rng", sa.Rng, sb.Rng);
        for (int i = 0; i < Math.Min(sa.CurrentRooms.Count, sb.CurrentRooms.Count); i++)
        {
            Compare($"room {i} ({sa.CurrentRooms[i].GetType().Name})", sa.CurrentRooms[i], sb.CurrentRooms[i]);
        }
        for (int s = 0; s < Math.Min(sa.RewardsStack.Count, sb.RewardsStack.Count); s++)
        {
            var (ra, rb) = (sa.RewardsStack[s].Rewards, sb.RewardsStack[s].Rewards);
            for (int k = 0; k < Math.Min(ra.Count, rb.Count); k++)
            {
                Compare($"rewards set {s} reward {k} ({ra[k].GetType().Name})", ra[k], rb[k]);
            }
        }
        Compare("combat", sa.Combat, sb.Combat);
        return parts.Count == 0 ? "no top-level part differs" : "differs in: " + string.Join(", ", parts);
    }

    public static void Test_SelfConsistency_F7_InterchangeableCopiesGiveEqualProjections()
    {
        int pairs = 0;
        var legalA = new List<GameAction>(32);
        var legalB = new List<GameAction>(32);
        foreach (var encounter in new[] { "NIBBITS_WEAK", "INKLETS_NORMAL", "FROG_KNIGHT_NORMAL" })
        {
            for (ulong seed = 21; seed < 27; seed++)
            {
                var sim = CombatSim.Create(seed, StartingDecks.Ironclad, encounter);
                var hand = sim.State.Player.Hand.Cards;
                for (int i = 0; i + 1 < hand.Count; i++)
                {
                    if (!Interchangeable(sim.State, hand[i], hand[i + 1]))
                    {
                        continue;
                    }
                    sim.LegalActions(legalA);
                    foreach (var play in legalA.Where(a => a.Kind == ActionKind.PlayCard && a.CardIndex == i).ToList())
                    {
                        var other = play with { CardIndex = i + 1 };
                        Assert.True(sim.IsLegal(other), $"{encounter}/{seed}: the copy at {i + 1} is playable like the copy at {i}");
                        var a = sim.Clone();
                        var b = sim.Clone();
                        a.Step(play);
                        b.Step(other);
                        string label = $"{encounter}/{seed} {play} vs {other}";
                        var policy = new Policy(seed * 31UL + (ulong)i);
                        for (int step = 0; step < 40; step++)
                        {
                            Assert.Equal(ProjectionDigest(a), ProjectionDigest(b), $"{label}: projection after {step} continuation steps");
                            if (a.IsTerminal || b.IsTerminal)
                            {
                                Assert.Equal(a.IsTerminal, b.IsTerminal, label + ": both branches end together");
                                break;
                            }
                            a.LegalActions(legalA);
                            b.LegalActions(legalB);
                            Assert.SequenceEqual(legalA, legalB, $"{label}: legal actions after {step} continuation steps");
                            var next = legalA[policy.Next(legalA.Count)];
                            a.Step(next);
                            b.Step(next);
                        }
                        pairs++;
                    }
                }
            }
        }
        Assert.True(pairs > 0, "the starter deck offered interchangeable copies");
        Console.WriteLine($"     SelfConsistency F7 copies: {pairs} interchangeable-copy plays gave equal projection and RNG streams");
    }

    public static void Test_SelfConsistency_F8_StandaloneCombatMatchesRunCombat()
    {
        int combats = 0;
        long lockstep = 0;
        foreach (var seed in WalkSeeds)
        {
            RunPhase? previous = null;
            Walk(NoNeowConfig(seed), seed, WalkStepCap, avoidEvents: true, before: (sim, step) =>
            {
                bool entered = sim.Phase == RunPhase.Combat && previous != RunPhase.Combat && sim.State.Combat is { IsInProgress: true } && !sim.HasPendingChoice;
                previous = sim.Phase;
                if (!entered)
                {
                    return;
                }
                lockstep += CheckCrossMode(sim.Clone(), new Policy((ulong)seed * 1000UL + (ulong)step), $"run {seed} combat at step {step}");
                combats++;
            });
        }
        Assert.True(combats > 0, "the walks entered combats");
        Console.WriteLine($"     SelfConsistency F8: {combats} run combats rebuilt standalone, {lockstep} lockstep steps with equal combat hashes");
    }

    private static long CheckCrossMode(RunSim run, Policy policy, string label)
    {
        var rs = run.State;
        var player = rs.Players[0].DeepClone();
        var standaloneRun = RunState.CreateStandalone(player, rs.Rng.DeepClone(), rs.Ascension, rs.Unlocks);
        standaloneRun.NextCardInstanceId = rs.NextCardInstanceId;
        var cs = rs.Combat!.CloneInto(standaloneRun, rs);
        standaloneRun.Combat = cs;
        var standalone = CombatSim.FromState(cs);
        var legal = new List<GameAction>(32);
        long steps = 0;
        for (int step = 0; step < 300; step++)
        {
            Assert.Equal(CombatDigest(rs.Combat!), CombatDigest(standalone.State), $"{label}: combat hash after {step} lockstep steps");
            standalone.LegalActions(legal);
            var runLegal = run.LegalActions().ToList();
            Assert.SequenceEqual(legal.Select(RunAction.FromCombat).ToList(), runLegal, $"{label}: legal actions after {step} lockstep steps");
            var action = legal[policy.Next(legal.Count)];
            run.Step(RunAction.FromCombat(action));
            standalone.Step(action);
            steps++;
            while (run.HasPendingChoice || standalone.HasPendingChoice)
            {
                Assert.Equal(run.HasPendingChoice, standalone.HasPendingChoice, $"{label}: both modes pause at a prompt after {step} steps");
                Assert.Equal(run.PendingChoice!.SiteId, standalone.PendingChoice!.SiteId, $"{label}: same prompt site");
                standalone.LegalActions(legal);
                Assert.SequenceEqual(legal.Select(RunAction.FromCombat).ToList(), run.LegalActions().ToList(), $"{label}: same prompt answers");
                var answer = legal[policy.Next(legal.Count)];
                run.Step(RunAction.FromCombat(answer));
                standalone.Step(answer);
                steps++;
            }
            rs = run.State;
            if (standalone.IsTerminal || run.Phase != RunPhase.Combat)
            {
                Assert.True(standalone.IsTerminal, $"{label}: the standalone combat ends when the run leaves combat");
                Assert.True(run.Phase != RunPhase.Combat, $"{label}: the run leaves combat when the standalone combat ends");
                Assert.Equal(standalone.State.Player.CurrentHp, run.State.Players[0].CurrentHp, $"{label}: player HP at combat end");
                return steps;
            }
        }
        return steps;
    }

    private static ulong CombatDigest(CombatState cs)
    {
        var w = new CanonicalWriter();
        cs.WriteCombatFields(w);
        cs.Player.WriteCanonical(w);
        cs.Run.Rng.WriteCanonical(w);
        w.WriteInt32(cs.Run.NextCardInstanceId);
        return StateHasher.Hash(w.WrittenSpan);
    }

    private static string ProjectionDigest(CombatSim sim)
    {
        var cs = sim.State;
        var projection = ProjectionJson.Write(SimProjector.Project(cs)).ToJsonString();
        var w = new CanonicalWriter();
        cs.Run.Rng.WriteCanonical(w);
        w.WriteByte((byte)cs.Outcome);
        w.WriteInt32(cs.RoundNumber);
        return projection + " rng " + StateHasher.Hash(w.WrittenSpan).ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool Interchangeable(CombatState cs, CardInstance a, CardInstance b)
        => ReferenceEquals(a.Def, b.Def) && a.UpgradeLevel == b.UpgradeLevel && a.Enchantment == null && b.Enchantment == null
           && a.Affliction == null && b.Affliction == null && a.Extra == null && b.Extra == null
           && SimProjector.CostOf(cs, a) == SimProjector.CostOf(cs, b) && SimProjector.CanPlay(cs, a) == SimProjector.CanPlay(cs, b);

    private static bool IsEventPoint(MapPoint point) => point.PointType is MapPointType.Unknown or MapPointType.Ancient;

    private static void CheckClosure(RunSim sim, string label, int step, ClosureCounts counts, bool avoidEvents = true)
    {
        counts.States++;
        counts.Phase(sim.Phase);
        var legal = sim.LegalActions().ToList();
        Assert.False(sim.IsTerminal, label + ": the walker only checks non-terminal states here");
        Assert.True(legal.Count > 0, $"{label} ({sim.Phase}): a non-terminal state has a legal action");
        Assert.Equal(legal.Count, legal.Distinct().Count(), $"{label}: legal actions are distinct");
        if (sim.HasPendingChoice)
        {
            counts.Prompts++;
            Assert.True(legal.Any(a => a.Kind is RunActionKind.PromptSelect or RunActionKind.PromptConfirm or RunActionKind.PromptCancel), $"{label}: a pending prompt has a legal answer");
        }
        ulong hash = sim.StateHash();
        var points = new List<MapPoint>(8);
        if (avoidEvents && sim.Phase == RunPhase.Map && !sim.HasPendingChoice)
        {
            RunLegalActionGenerator.GetLegalMapPoints(sim.State, points);
        }
        foreach (var action in legal)
        {
            Assert.True(sim.IsLegal(action), $"{label}: listed action {action} is legal");
            if (action.Kind == RunActionKind.Travel && points.Count > 0 && IsEventPoint(points[action.A]))
            {
                counts.SkippedEventTravel++;
                continue;
            }
            var clone = sim.Clone();
            try
            {
                clone.Step(action);
                counts.Stepped++;
                if (!clone.IsTerminal)
                {
                    Assert.True(clone.LegalActions().Count > 0, $"{label}: after {action} the next non-terminal state has a legal action");
                }
            }
            catch (NotImplementedException ex)
            {
                counts.Gap(ex.Message);
            }
        }
        Assert.Equal(hash, sim.StateHash(), $"{label}: stepping clones never changes the source");
        var candidates = Candidates(legal);
        bool full = step % 3 == 0;
        int illegalStepped = 0;
        foreach (var candidate in candidates)
        {
            bool listed = legal.Contains(candidate);
            if (full)
            {
                counts.Candidates++;
                Assert.Equal(listed, sim.IsLegal(candidate), $"{label}: IsLegal({candidate}) agrees with the legal list");
            }
            if (!listed && illegalStepped < 2 && (full || candidate.Kind == legal[0].Kind))
            {
                bool threw = false;
                try
                {
                    sim.Step(candidate);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    threw = true;
                }
                Assert.True(threw, $"{label}: illegal {candidate} is rejected");
                Assert.Equal(hash, sim.StateHash(), $"{label}: rejecting illegal {candidate} leaves the state unchanged");
                counts.Illegal++;
                illegalStepped++;
            }
        }
    }

    private static void CheckTerminal(RunSim sim, string label, ClosureCounts counts)
    {
        if (!sim.IsTerminal)
        {
            return;
        }
        counts.Terminal++;
        Assert.Equal(0, sim.LegalActions().Count, label + ": a terminal state has no legal action");
        Assert.False(sim.IsLegal(RunAction.Of(RunActionKind.EndTurn)), label + ": nothing is legal at a terminal state");
    }

    private static List<RunAction> Candidates(List<RunAction> legal)
    {
        int maxA = legal.Count == 0 ? 0 : legal.Max(a => a.A);
        var indices = new[] { RunAction.NoIndex, 0, 1, 2, maxA, maxA + 1, 99 }.Distinct().ToArray();
        var targets = new[] { RunAction.NoIndex, 0, 1, 5 };
        var list = new List<RunAction>(Enum.GetValues<RunActionKind>().Length * indices.Length * targets.Length);
        foreach (var kind in Enum.GetValues<RunActionKind>())
        {
            foreach (var a in indices)
            {
                foreach (var b in targets)
                {
                    list.Add(new RunAction(kind, a, b));
                }
            }
        }
        return list;
    }

    private static RunConfig NoNeowConfig(int seed)
        => new("SELFCHECK" + seed.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), StartingDecks.IroncladCharacterId, 0)
        {
            Unlocks = NoNeowUnlocks,
            Content = ContentFilter.PortedOnly
        };

    private static RunConfig DefaultConfig(int seed)
        => new("SELFCHECK" + seed.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), StartingDecks.IroncladCharacterId, 0)
        {
            Content = ContentFilter.PortedOnly
        };

    private sealed class RunWalk
    {
        public RunWalk(RunSim sim)
        {
            Sim = sim;
        }

        public RunSim Sim { get; }

        public List<RunAction> Actions { get; } = new();

        public List<ulong> Hashes { get; } = new();

        public string? Stop { get; set; }
    }

    private static RunWalk Walk(RunConfig config, int policySeed, int stepCap, bool avoidEvents,
        Action<RunSim>? start = null, Action<RunSim, int>? before = null, Action<RunSim, int>? after = null, bool rethrowGaps = false)
    {
        var walk = new RunWalk(RunSim.NewRun(config));
        var sim = walk.Sim;
        if (avoidEvents && !sim.HasPendingChoice)
        {
            var player = sim.State.Players[0];
            player.MaxHp = WalkMaxHp;
            player.CurrentHp = WalkMaxHp;
        }
        start?.Invoke(sim);
        var policy = new Policy((ulong)policySeed * 0x9E37UL + 7UL);
        var points = new List<MapPoint>(8);
        var choices = new List<RunAction>(32);
        for (int step = 0; step < stepCap && !sim.IsTerminal; step++)
        {
            before?.Invoke(sim, step);
            var legal = sim.LegalActions();
            choices.Clear();
            if (avoidEvents && sim.Phase == RunPhase.Map && !sim.HasPendingChoice)
            {
                RunLegalActionGenerator.GetLegalMapPoints(sim.State, points);
                bool hadTravel = false;
                foreach (var action in legal)
                {
                    if (action.Kind != RunActionKind.Travel)
                    {
                        continue;
                    }
                    hadTravel = true;
                    if (!IsEventPoint(points[action.A]))
                    {
                        choices.Add(action);
                    }
                }
                if (hadTravel && choices.Count == 0)
                {
                    walk.Stop = "only event map points ahead";
                    return walk;
                }
                if (!hadTravel)
                {
                    choices.AddRange(legal);
                }
            }
            else if (sim.Phase == RunPhase.Combat && !sim.HasPendingChoice && policy.Next(8) != 0 && legal.Any(a => a.Kind == RunActionKind.PlayCard))
            {
                choices.AddRange(legal.Where(a => a.Kind == RunActionKind.PlayCard));
            }
            else
            {
                choices.AddRange(legal.Where(a => a.Kind != RunActionKind.DiscardPotion));
                if (choices.Count == 0)
                {
                    choices.AddRange(legal);
                }
            }
            var chosen = choices[policy.Next(choices.Count)];
            try
            {
                sim.Step(chosen);
            }
            catch (NotImplementedException ex) when (!rethrowGaps)
            {
                walk.Stop = "gap: " + ex.Message;
                return walk;
            }
            walk.Actions.Add(chosen);
            walk.Hashes.Add(sim.StateHash());
            after?.Invoke(sim, step);
        }
        if (!sim.IsTerminal)
        {
            walk.Stop = "step cap";
        }
        return walk;
    }

    private sealed class ClosureCounts
    {
        private readonly Dictionary<string, int> _stops = new(StringComparer.Ordinal);

        private readonly SortedDictionary<RunPhase, int> _phases = new();

        public void Phase(RunPhase phase) => _phases[phase] = _phases.GetValueOrDefault(phase) + 1;

        public long States;
        public long Prompts;
        public long Stepped;
        public long Candidates;
        public long Illegal;
        public long Terminal;
        public long Steps;
        public long SkippedEventTravel;

        public void Gap(string message) => Add("listed action hit gap: " + message);

        public void Note(RunWalk walk)
        {
            Steps += walk.Actions.Count;
            Add(walk.Sim.IsTerminal ? walk.Sim.Phase.ToString() : walk.Stop ?? "?");
        }

        private void Add(string key) => _stops[key] = _stops.GetValueOrDefault(key) + 1;

        public string Describe() => $"{Steps} walk steps; " + (_phases.Count == 0 ? string.Empty : "phases: " + string.Join(", ", _phases.Select(p => $"{p.Key} {p.Value}")) + "; ") + "endings: " + string.Join(", ", _stops.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key} x{p.Value}"));
    }

    private sealed class RunLedger
    {
        private readonly int[] _runRng = new int[RunRngSet.StreamCount];
        private readonly int[] _playerRng = new int[PlayerRngSet.StreamCount];
        private readonly HashSet<int> _goneCardIds = new();
        private HashSet<int>? _cardIds;
        private bool _has;
        private int _nextCardInstanceId = int.MinValue;
        private int _nextCreatureId = int.MinValue;

        public long Checks { get; private set; }

        public void Check(RunSim sim, string label)
        {
            Checks++;
            var rs = sim.State;
            Assert.Equal(1, rs.Players.Count, label + ": one player");
            var player = rs.Players[0];
            Assert.True(player.CurrentHp >= 0 && player.CurrentHp <= player.MaxHp, $"{label}: player HP {player.CurrentHp} outside [0, {player.MaxHp}]");
            Assert.True(player.MaxHp > 0, $"{label}: player max HP {player.MaxHp}");
            Assert.True(player.Block >= 0, $"{label}: player block {player.Block}");
            Assert.True(player.Energy >= 0, $"{label}: energy {player.Energy}");
            Assert.True(player.Stars >= 0, $"{label}: stars {player.Stars}");
            Assert.True(player.Gold >= 0, $"{label}: gold {player.Gold}");
            var potions = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var potion in player.PotionSlots)
            {
                if (potion == null)
                {
                    continue;
                }
                Assert.True(potions.Add(potion), $"{label}: potion {potion.Id} fills two slots");
                Assert.True(potion.HasOwner && ReferenceEquals(potion.Owner, player), $"{label}: potion {potion.Id} is not owned by the player");
            }
            Assert.True(potions.Count <= player.MaxPotionCount, $"{label}: {potions.Count} potions in {player.MaxPotionCount} slots");
            foreach (var relic in player.Relics)
            {
                Assert.True(relic.HasOwner && ReferenceEquals(relic.Owner, player), $"{label}: relic {relic.Id} is not owned by the player");
            }
            var objects = new HashSet<CardInstance>(ReferenceEqualityComparer.Instance);
            var ids = new HashSet<int>();
            foreach (var card in player.Deck)
            {
                Assert.True(objects.Add(card), $"{label}: deck card {card.Id}#{card.InstanceId} appears twice");
                Assert.True(ids.Add(card.InstanceId), $"{label}: deck card {card.Id}#{card.InstanceId} shares its id");
                Assert.True(!card.HasOwner || ReferenceEquals(card.Owner, player), $"{label}: deck card {card.Id} is owned by another player object");
            }
            var cs = rs.Combat;
            if (cs != null)
            {
                Assert.True(ReferenceEquals(cs.Player, player), $"{label}: the combat's player is the run's player");
                foreach (var pile in player.AllPiles)
                {
                    foreach (var card in pile.Cards)
                    {
                        string which = $"{label}: {pile.Type} card {card.Id}#{card.InstanceId}";
                        Assert.True(objects.Add(card), $"{which} is in two piles or also in the deck");
                        Assert.True(ids.Add(card.InstanceId), $"{which} shares its instance id");
                        Assert.True(!card.HasOwner || ReferenceEquals(card.Owner, player), $"{which} is owned by another player object");
                    }
                }
                var creatureIds = new HashSet<int>();
                var powers = new HashSet<PowerModel>(ReferenceEqualityComparer.Instance);
                var creatures = new List<CreatureState>();
                foreach (var c in cs.Allies.Concat<CreatureState>(cs.Monsters))
                {
                    if (!creatures.Any(x => ReferenceEquals(x, c)))
                    {
                        creatures.Add(c);
                    }
                }
                Assert.True(cs.NextCreatureId >= _nextCreatureId, $"{label}: NextCreatureId went down from {_nextCreatureId} to {cs.NextCreatureId}");
                _nextCreatureId = cs.NextCreatureId;
                foreach (var creature in creatures)
                {
                    string who = $"{label}: creature {creature.CombatId}";
                    Assert.True(creature.CurrentHp >= 0 && creature.CurrentHp <= creature.MaxHp, $"{who} HP {creature.CurrentHp} outside [0, {creature.MaxHp}]");
                    Assert.True(creature.Block >= 0, $"{who} block {creature.Block}");
                    Assert.True(creatureIds.Add(creature.CombatId), $"{who} id is used twice");
                    foreach (var power in creature.Powers)
                    {
                        Assert.True(power.HasOwner && ReferenceEquals(power.Owner, creature), $"{who} holds {power.Id} owned by another creature object");
                        Assert.True(powers.Add(power), $"{who} holds {power.Id}, which another creature also holds");
                    }
                }
            }
            else
            {
                _nextCreatureId = int.MinValue;
                foreach (var pile in player.AllPiles)
                {
                    Assert.Equal(0, pile.Cards.Count, $"{label}: {pile.Type} is empty out of combat");
                }
            }
            foreach (var id in ids)
            {
                Assert.True(id < rs.NextCardInstanceId, $"{label}: card id {id} was not allocated (next {rs.NextCardInstanceId})");
                Assert.False(_goneCardIds.Contains(id), $"{label}: card id {id} left every pile and the deck earlier and came back");
            }
            if (_cardIds != null)
            {
                foreach (var id in _cardIds)
                {
                    if (!ids.Contains(id))
                    {
                        _goneCardIds.Add(id);
                    }
                }
            }
            _cardIds = ids;
            Assert.True(rs.NextCardInstanceId >= _nextCardInstanceId, $"{label}: NextCardInstanceId went down from {_nextCardInstanceId} to {rs.NextCardInstanceId}");
            _nextCardInstanceId = rs.NextCardInstanceId;
            for (int i = 0; i < _runRng.Length; i++)
            {
                int counter = rs.Rng.Get((RunRngType)i).Counter;
                Assert.True(!_has || counter >= _runRng[i], $"{label}: run RNG {RunRngSet.StreamName((RunRngType)i)} went down from {_runRng[i]} to {counter}");
                _runRng[i] = counter;
            }
            if (player.PlayerRng is { } prng)
            {
                for (int i = 0; i < _playerRng.Length; i++)
                {
                    int counter = prng.Get((PlayerRngType)i).Counter;
                    Assert.True(!_has || counter >= _playerRng[i], $"{label}: player RNG {PlayerRngSet.StreamName((PlayerRngType)i)} went down from {_playerRng[i]} to {counter}");
                    _playerRng[i] = counter;
                }
            }
            _has = true;
        }
    }

    private sealed class Policy
    {
        private ulong _state;

        public Policy(ulong seed)
        {
            _state = seed;
        }

        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return (int)((z ^ (z >> 31)) % (ulong)maxExclusive);
            }
        }
    }

    private sealed record CombatStreamResult(List<ulong> Hashes);

    private static CombatStreamResult CombatStream(string encounter, ulong seed)
    {
        var sim = CombatSim.Create(seed, StartingDecks.Ironclad, encounter);
        var policy = new Policy(seed ^ 0xF2UL);
        var hashes = new List<ulong> { sim.StateHash() };
        var legal = new List<GameAction>(32);
        for (int step = 0; step < 300 && !sim.IsTerminal; step++)
        {
            sim.LegalActions(legal);
            sim.Step(legal[policy.Next(legal.Count)]);
            hashes.Add(sim.StateHash());
        }
        return new CombatStreamResult(hashes);
    }

    private static ulong Digest(IReadOnlyList<ulong> hashes)
    {
        var bytes = new byte[hashes.Count * 8];
        for (int i = 0; i < hashes.Count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), hashes[i]);
        }
        return StateHasher.Hash(bytes);
    }

    private static readonly string[] IgnoredPaths =
    {
        "players[0].net_id"
    };

    private static bool IsIgnored(string line)
    {
        int eq = line.IndexOf('=');
        string path = eq < 0 ? line : line[..eq];
        string pattern = System.Text.RegularExpressions.Regex.Replace(path, @"deck\[\d+\]", "deck[*]");
        return IgnoredPaths.Contains(pattern, StringComparer.Ordinal);
    }

    private static void AssertSameLines(List<string> expected, List<string> actual, string label)
    {
        int n = Math.Max(expected.Count, actual.Count);
        for (int i = 0; i < n; i++)
        {
            string e = i < expected.Count ? expected[i] : "<missing>";
            string a = i < actual.Count ? actual[i] : "<missing>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                Assert.Fail($"{label}: line {i} differs\n  expected {Clip(e)}\n  actual   {Clip(a)}");
            }
        }
    }

    private static string Clip(string s) => s.Length <= 400 ? s : s[..400] + "...";

    private static string FixturePath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", "run_save_a8_silent_boss1.save");

    private static SavedRun RoundTripFixture()
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        var player = root["players"]![0]!.AsObject();
        var deck = player["deck"]!.AsArray();
        for (int i = deck.Count - 1; i >= 0; i--)
        {
            var id = RunSaveReader.StripCategory((string)deck[i]!["id"]!);
            bool ported = CardRegistry.TryGet(id, out var def) && def is not UnportedCardDef && deck[i]!["props"] == null;
            var ench = deck[i]!["enchantment"];
            if (ench != null && !EnchantmentRegistry.Models.Contains(RunSaveReader.StripCategory((string)ench["id"]!)))
            {
                ported = false;
            }
            if (!ported)
            {
                deck.RemoveAt(i);
            }
        }
        var relics = player["relics"]!.AsArray();
        for (int i = relics.Count - 1; i >= 0; i--)
        {
            var id = RunSaveReader.StripCategory((string)relics[i]!["id"]!);
            if (!RelicRegistry.Models.Contains(id) || relics[i]!["props"] != null)
            {
                relics.RemoveAt(i);
            }
        }
        foreach (var act in root["acts"]!.AsArray())
        {
            var rooms = act!["rooms"]!.AsObject();
            foreach (var key in new[] { "normal_encounter_ids", "elite_encounter_ids" })
            {
                var list = rooms[key]!.AsArray();
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (!EncounterRegistry.Defs.Contains(RunSaveReader.StripCategory((string)list[i]!)))
                    {
                        list.RemoveAt(i);
                    }
                }
            }
        }
        return RunSaveReader.Read(root.ToJsonString());
    }
}
