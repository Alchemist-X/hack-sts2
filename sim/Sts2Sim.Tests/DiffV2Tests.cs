using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;
using Sts2Sim.Diff.V2;

namespace Sts2Sim.Tests;

public static class DiffV2Tests
{
    private const string EncounterId = "NIBBITS_WEAK";
    private const string Seed = "G0G2JMFEYHRS";
    private const string RunSeed = "V2RUNSEED1";
    private const string Version = "v0.111.0";

    private static string FixturePath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", "run_save_a8_silent_boss1.save");

    private static JsonObject V2Shell(string seed = Seed) => new()
    {
        ["bundle_version"] = 2,
        ["kind"] = "run",
        ["source"] = new JsonObject { ["lineage"] = "TEST-" + seed, ["provenance"] = new JsonObject { ["driver"] = "sim" } },
        ["game_version"] = Version,
        ["rules_version"] = Version,
        ["seeding"] = "xxh64",
        ["seed"] = seed,
        ["character"] = StartingDecks.IroncladCharacterId,
        ["ascension"] = 0,
        ["game_mode"] = "standard",
        ["modifiers"] = new JsonArray(),
        ["anchors"] = new JsonArray(),
        ["segments"] = new JsonArray()
    };

    private static JsonObject CombatStart(IEnumerable<string> deck, JsonArray? potions = null)
    {
        var deckJson = new JsonArray();
        foreach (var id in deck)
        {
            deckJson.Add(new JsonObject { ["id"] = id, ["upgraded"] = false });
        }
        var start = new JsonObject
        {
            ["encounter_id"] = EncounterId,
            ["player"] = new JsonObject { ["hp"] = 70, ["max_hp"] = 80 },
            ["deck"] = deckJson,
            ["relics"] = new JsonArray("BURNING_BLOOD"),
            ["rng_offsets"] = new JsonObject()
        };
        if (potions != null)
        {
            start["potions"] = potions.DeepClone();
        }
        return start;
    }

    private static JsonObject CombatPreState(CombatState cs, ChoicePromptView? prompt)
    {
        var pre = ProjectionJson.Write(SimProjector.Project(cs));
        pre["player"]!["potions"] = RunScreenProjector.Player(cs.Player)["potions"]!.DeepClone();
        if (prompt != null)
        {
            pre["select"] = RunScreenProjector.Select(prompt.Prompt, prompt.Picks);
        }
        return pre;
    }

    private sealed record ChoicePromptView(Sts2Sim.Core.Choice.ChoicePrompt Prompt, IReadOnlyList<int> Picks);

    private static JsonObject CombatActionJson(CombatState cs, GameAction a)
    {
        switch (a.Kind)
        {
            case ActionKind.PlayCard:
                var card = cs.Player.Hand[a.CardIndex];
                return new JsonObject
                {
                    ["type"] = "play_card",
                    ["hand_index"] = a.CardIndex,
                    ["card_id"] = card.Id,
                    ["upgraded"] = card.IsUpgraded,
                    ["target_combat_id"] = a.TargetIndex >= 0 ? JsonValue.Create(cs.Monsters[a.TargetIndex].CombatId) : null
                };
            case ActionKind.UsePotion:
                return new JsonObject
                {
                    ["type"] = "use_potion",
                    ["slot"] = a.CardIndex,
                    ["potion_id"] = cs.Player.PotionSlots[a.CardIndex]!.Id,
                    ["target_combat_id"] = a.TargetIndex >= 0 ? JsonValue.Create(cs.Monsters[a.TargetIndex].CombatId) : null,
                    ["in_combat"] = true
                };
            case ActionKind.DiscardPotion:
                return new JsonObject { ["type"] = "discard_potion", ["slot"] = a.CardIndex, ["potion_id"] = cs.Player.PotionSlots[a.CardIndex]!.Id };
            default:
                return new JsonObject { ["type"] = "end_turn" };
        }
    }

    private static GameAction Greedy(IReadOnlyList<GameAction> legal, Rng? policy)
    {
        if (policy != null)
        {
            return legal[policy.NextInt(legal.Count)];
        }
        foreach (var a in legal)
        {
            if (a.Kind is ActionKind.UsePotion)
            {
                return a;
            }
        }
        foreach (var a in legal)
        {
            if (a.Kind is ActionKind.PlayCard)
            {
                return a;
            }
        }
        return GameAction.EndTurn;
    }

    private static (JsonObject Choose, JsonObject Pre) RecordPrompt(Func<Sts2Sim.Core.Choice.ChoicePrompt?> prompt, Func<IReadOnlyList<int>> picks, Func<IReadOnlyList<GameAction>> legal,
        Action<GameAction> step, Func<JsonObject> pre)
    {
        var p = prompt()!;
        var preState = pre();
        var picked = new JsonArray();
        while (prompt() is { } open && ReferenceEquals(open, p))
        {
            var options = legal();
            var pick = options.FirstOrDefault(x => x.Kind == ActionKind.ChoosePick);
            if (options.Contains(GameAction.Confirm) && (pick.Kind != ActionKind.ChoosePick || picks().Count >= p.MinSelect && picks().Count > 0))
            {
                step(GameAction.Confirm);
                break;
            }
            picked.Add(new JsonObject { ["index"] = pick.CardIndex, ["card_id"] = p.Options[pick.CardIndex].ModelId });
            step(pick);
        }
        return (new JsonObject { ["type"] = "choose", ["context"] = "combat", ["picks"] = picked }, preState);
    }

    private static JsonObject RecordCombatSegment(IEnumerable<string> deck, JsonArray? potions = null, ulong? policySeed = null, int maxDecisions = 300)
    {
        var shell = V2Shell();
        var start = CombatStart(deck, potions);
        var tmp = (JsonObject)shell.DeepClone();
        tmp["segments"] = new JsonArray(new JsonObject { ["id"] = "c1", ["kind"] = "combat", ["act"] = 1, ["floor"] = 2, ["room_model_id"] = EncounterId, ["start"] = start.DeepClone(), ["decisions"] = new JsonArray() });
        var parsed = RunBundleJson.Parse(tmp.ToJsonString());
        var sim = BundleRunner.CreateSim(parsed.Segments[0].CombatStart!, V2Runner.StartExtras(parsed.Segments[0]));
        var policy = policySeed is ulong ps ? new Rng(ps) : null;
        var decisions = new JsonArray();
        while (!sim.IsTerminal && decisions.Count < maxDecisions)
        {
            JsonObject action;
            JsonObject pre;
            if (sim.PendingChoice != null)
            {
                (action, pre) = RecordPrompt(() => sim.PendingChoice, () => sim.PendingPicks, () => sim.LegalActions(), sim.Step,
                    () => CombatPreState(sim.State, new ChoicePromptView(sim.PendingChoice!, sim.PendingPicks.ToArray())));
            }
            else
            {
                pre = CombatPreState(sim.State, null);
                var a = Greedy(sim.LegalActions(), policy);
                action = CombatActionJson(sim.State, a);
                sim.Step(a);
            }
            decisions.Add(new JsonObject { ["index"] = decisions.Count, ["screen"] = "combat", ["pre_state"] = pre, ["action"] = action });
        }
        shell["segments"] = new JsonArray(new JsonObject
        {
            ["id"] = "c1",
            ["kind"] = "combat",
            ["act"] = 1,
            ["floor"] = 2,
            ["room_type"] = "monster",
            ["room_model_id"] = EncounterId,
            ["attempt"] = 1,
            ["start"] = start,
            ["decisions"] = decisions,
            ["outcome"] = new JsonObject { ["result"] = BundleRunner.OutcomeName(sim.Outcome), ["final_hp"] = BundleRunner.FinalHp(sim.State) }
        });
        return shell;
    }

    private static RunSim SeedRun() => AnchorLoader.SeedStart(RunBundleJson.Parse(V2Shell(RunSeed).ToJsonString()), SeedStartJson());

    private static JsonObject SeedStartJson() => new() { ["from"] = "seed", ["content"] = "ported_only", ["neow"] = false };

    private static JsonObject RunActionJson(RunSim sim, RunAction a)
    {
        if (a.TryToCombat(out var ga) && sim.State.Combat is { } cs && sim.Phase == RunPhase.Combat)
        {
            return CombatActionJson(cs, ga);
        }
        switch (a.Kind)
        {
            case RunActionKind.Travel:
                var points = new List<MapPoint>();
                RunLegalActionGenerator.GetLegalMapPoints(sim.State, points);
                return new JsonObject { ["type"] = "map_move", ["col"] = points[a.A].coord.col, ["row"] = points[a.A].coord.row };
            case RunActionKind.ClaimReward:
                var reward = RunLegalActionGenerator.TopRewards(sim.State)!.Rewards[a.A];
                return new JsonObject { ["type"] = "reward_take", ["index"] = a.A, ["reward_type"] = RunScreenProjector.RewardTypeName(reward.RewardType) };
            case RunActionKind.ProceedRewards:
                return new JsonObject { ["type"] = "rewards_skip" };
            default:
                throw new InvalidOperationException($"the test recorder does not map {a}");
        }
    }

    private static RunAction RunPolicy(RunSim sim)
    {
        var legal = sim.LegalActions();
        switch (sim.Phase)
        {
            case RunPhase.Combat:
                var ga = Greedy(legal.Where(x => x.TryToCombat(out _)).Select(x => { x.TryToCombat(out var g); return g; }).ToList(), null);
                return RunAction.FromCombat(ga);
            case RunPhase.Rewards:
                var set = RunLegalActionGenerator.TopRewards(sim.State)!;
                foreach (var a in legal)
                {
                    if (a.Kind == RunActionKind.ClaimReward && set.Rewards[a.A].RewardType is RewardType.Gold or RewardType.Potion)
                    {
                        return a;
                    }
                }
                return RunAction.Of(RunActionKind.ProceedRewards);
            default:
                return legal[0];
        }
    }

    private static JsonObject RecordRunSegments(int decisionsPerSegment, int segments, out RunSim final)
    {
        var shell = V2Shell(RunSeed);
        var sim = SeedRun();
        var segs = new JsonArray();
        for (int s = 0; s < segments; s++)
        {
            var decisions = new JsonArray();
            while (decisions.Count < decisionsPerSegment && !sim.IsTerminal && sim.Phase is RunPhase.Map or RunPhase.Combat or RunPhase.Rewards && !sim.HasPendingChoice)
            {
                var pre = RunScreenProjector.Project(sim);
                string screen = RunScreenProjector.ScreenOf(sim);
                var a = RunPolicy(sim);
                var action = RunActionJson(sim, a);
                if (pre["map"] is JsonObject map)
                {
                    map.Remove("nodes");
                }
                sim.Step(a);
                var decision = new JsonObject { ["index"] = decisions.Count, ["screen"] = screen, ["pre_state"] = pre, ["action"] = action };
                if (a.Kind == RunActionKind.Travel)
                {
                    decision["room_entered"] = RunScreenProjector.Room(RoomStack.CurrentRoom(sim.State))!.DeepClone();
                }
                decisions.Add(decision);
            }
            var seg = new JsonObject { ["id"] = "r" + s, ["kind"] = "room", ["decisions"] = decisions };
            if (s == 0)
            {
                seg["start"] = SeedStartJson();
            }
            if (s == segments - 1 && sim.State.MapPointHistory.Count > 0)
            {
                var summary = RunScreenProjector.FloorSummary(sim.State.MapPointHistory[^1]);
                if (summary["rooms"] is JsonArray rooms && rooms.Count > 0 && rooms[0]!["model_id"] is JsonValue mv)
                {
                    rooms[0]!["model_id"] = "ENCOUNTER." + mv.GetValue<string>();
                }
                summary["player_stats"] = new JsonArray();
                seg["floor_summary"] = summary;
            }
            segs.Add(seg);
        }
        shell["segments"] = segs;
        final = sim;
        return shell;
    }

    private static List<SegmentResult> Run(JsonObject bundle, V2Options? options = null)
        => V2Runner.RunBundle(RunBundleJson.Parse(bundle.ToJsonString(), "memory://test.json"), options);

    private static JsonObject Decision(JsonObject bundle, int segment, int decision)
        => bundle["segments"]![segment]!["decisions"]![decision]!.AsObject();

    public static void Test_DiffV2_V1FileReadsAsOneCombatSegment()
    {
        var sim = CombatSim.Create(RngSetup.Create(RngSetup.SeedingXxh64, Seed), StartingDecks.IroncladAtRunStart(0).Select(id => new CardSpec(id, false)).ToArray(), EncounterId, 0, 70, 80);
        var decisions = new List<BundleDecision>();
        var buffer = new List<GameAction>();
        while (!sim.IsTerminal && decisions.Count < 200)
        {
            var projection = SimProjector.Project(sim.State);
            sim.LegalActions(buffer);
            var a = Greedy(buffer, null);
            var card = a.Kind == ActionKind.PlayCard ? sim.State.Player.Hand[a.CardIndex] : null;
            var recorded = card == null ? BundleAction.EndTurn : BundleAction.Play(a.CardIndex, card.Id, card.IsUpgraded, a.TargetIndex >= 0 ? sim.State.Monsters[a.TargetIndex].CombatId : null);
            decisions.Add(new BundleDecision { Index = decisions.Count, PreState = projection, PreStateJson = ProjectionJson.Write(projection), Action = recorded });
            sim.Step(a);
        }
        var v1 = new DiffBundle
        {
            Source = new BundleSource("V1SESSION", "p", 1, 1, 2),
            GameVersion = Version,
            Seeding = RngSetup.SeedingXxh64,
            Seed = Seed,
            EncounterId = EncounterId,
            PlayerHp = 70,
            PlayerMaxHp = 80,
            Deck = StartingDecks.IroncladAtRunStart(0).Select(id => new BundleCard(id, false)).ToList(),
            Relics = new[] { "BURNING_BLOOD" },
            Decisions = decisions,
            Outcome = BundleRunner.OutcomeName(sim.Outcome),
            FinalHp = sim.State.Player.CurrentHp
        };
        string json = BundleJson.Write(v1).ToJsonString();
        var run = RunBundleJson.Parse(json, "memory://v1.json");
        Assert.True(run.FromV1, "a bundle_version 1 file reads as v2");
        Assert.Equal(1, run.Segments.Count);
        Assert.Equal("combat", run.Segments[0].Kind);
        Assert.Equal("V1SESSION", run.Lineage);
        var v1Result = BundleRunner.Run(BundleJson.Parse(json));
        var v2Result = V2Runner.RunBundle(run).Single();
        Assert.Equal(BundleStatus.Agree, v1Result.Status);
        Assert.Equal(BundleStatus.Agree, v2Result.Status, v2Result.Reason);
        Assert.Equal(v1Result.DecisionsCompared, v2Result.DecisionsCompared);
        Assert.Equal(v1Result.DecisionsAgreed, v2Result.DecisionsAgreed);
        Assert.Equal(v1Result.SimFinalHp, v2Result.SimFinalHp);
        Assert.True(v2Result.OutcomeChecked && v2Result.OutcomeAgreed);
        Assert.Equal("v1", v2Result.StartMode);
    }

    public static void Test_DiffV2_ReaderValidatesSchema()
    {
        var good = RecordCombatSegment(StartingDecks.IroncladAtRunStart(0), maxDecisions: 3);
        RunBundleJson.Parse(good.ToJsonString());
        void Bad(Action<JsonObject> edit, string pathFragment)
        {
            var b = (JsonObject)good.DeepClone();
            edit(b);
            var ex = Assert.Throws<BundleFormatException>(() => RunBundleJson.Parse(b.ToJsonString()));
            Assert.True(ex.Path.Contains(pathFragment, StringComparison.Ordinal), $"error path {ex.Path} should name {pathFragment}");
        }
        Bad(b => b["bundle_version"] = 3, "$.bundle_version");
        Bad(b => b["segments"]![0]!["kind"] = "bogus", "$.segments[0].kind");
        Bad(b => b["segments"]![0]!["decisions"]![1]!["index"] = 5, "$.segments[0].decisions[1].index");
        Bad(b => b["segments"]![0]!["decisions"]![0]!["screen"] = "nowhere", "$.segments[0].decisions[0].screen");
        Bad(b => b["segments"]![0]!["decisions"]![0]!["action"]!["type"] = "teleport", "$.segments[0].decisions[0].action.type");
        Bad(b => b["segments"]![0]!["decisions"]![0]!["action"] = new JsonObject { ["type"] = "map_move", ["col"] = 1, ["row"] = 1 }, "$.segments[0].decisions[0].action.type");
        Bad(b => b["segments"]![0]!["start_anchor"] = "a9", "$.segments[0]");
        Bad(b => b["segments"]!.AsArray().Add(b["segments"]![0]!.DeepClone()), "$.segments[1].id");
        Bad(b => b["seeding"] = "md5", "$.seeding");
        var withUnknown = (JsonObject)good.DeepClone();
        withUnknown["mystery"] = 1;
        Assert.True(RunBundleJson.Parse(withUnknown.ToJsonString()).IgnoredKeys.Contains("$.mystery"), "unknown keys are listed, not fatal");
    }

    public static void Test_DiffV2_CombatSegmentSelfReplayWithPromptsAndPotions()
    {
        var deck = new List<string>(StartingDecks.IroncladAtRunStart(0)) { "BURNING_PACT", "BURNING_PACT", "ARMAMENTS", "TRUE_GRIT" };
        var potions = new JsonArray(new JsonObject { ["slot"] = 0, ["id"] = "BLOCK_POTION" }, new JsonObject { ["slot"] = 1, ["id"] = "FIRE_POTION" });
        var bundle = RecordCombatSegment(deck, potions);
        var decisions = bundle["segments"]![0]!["decisions"]!.AsArray();
        Assert.True(decisions.Any(d => d!["action"]!["type"]!.GetValue<string>() == "choose"), "the recording reaches a selection prompt");
        Assert.True(decisions.Any(d => d!["action"]!["type"]!.GetValue<string>() == "use_potion"), "the recording uses a potion");
        var result = Run(bundle).Single();
        Assert.Equal(BundleStatus.Agree, result.Status, result.Reason);
        Assert.Equal(decisions.Count, result.DecisionsAgreed);
        Assert.True(result.OutcomeChecked && result.OutcomeAgreed);

        for (ulong seed = 1; seed <= 4; seed++)
        {
            var random = RecordCombatSegment(deck, potions, seed);
            var r = Run(random).Single();
            Assert.Equal(BundleStatus.Agree, r.Status, $"random policy {seed}: {r.Reason}");
        }

        var chooseIndex = decisions.Select((d, i) => (d, i)).First(x => x.d!["action"]!["type"]!.GetValue<string>() == "choose").i;
        var wrongPick = (JsonObject)bundle.DeepClone();
        var picks = Decision(wrongPick, 0, chooseIndex)["action"]!["picks"]!.AsArray();
        picks[0]!["card_id"] = "NOT_A_CARD";
        var pickResult = Run(wrongPick).Single();
        Assert.Equal(BundleStatus.HardDivergence, pickResult.Status);
        Assert.Equal("action.picks.card_id", pickResult.Divergence!.First.Path);

        var noPrompt = (JsonObject)bundle.DeepClone();
        Decision(noPrompt, 0, 0)["action"] = new JsonObject { ["type"] = "choose", ["picks"] = new JsonArray(new JsonObject { ["index"] = 0 }) };
        Decision(noPrompt, 0, 0)["pre_state"] = null;
        var noPromptResult = Run(noPrompt).Single();
        Assert.Equal("action.prompt", noPromptResult.Divergence!.First.Path);
        Assert.Equal(DivergenceClass.PortBug, noPromptResult.Class);

        var potionIndex = decisions.Select((d, i) => (d, i)).First(x => x.d!["action"]!["type"]!.GetValue<string>() == "use_potion").i;
        var wrongPotion = (JsonObject)bundle.DeepClone();
        Decision(wrongPotion, 0, potionIndex)["action"]!["potion_id"] = "SPEED_POTION";
        Assert.Equal("action.potion_id", Run(wrongPotion).Single().Divergence!.First.Path);
    }

    public static void Test_DiffV2_CombatDivergencesAreClassified()
    {
        var bundle = RecordCombatSegment(StartingDecks.IroncladAtRunStart(0));
        var hp = (JsonObject)bundle.DeepClone();
        Decision(hp, 0, 1)["pre_state"]!["player"]!["hp"] = 1;
        var r = Run(hp).Single();
        Assert.Equal(BundleStatus.HardDivergence, r.Status);
        Assert.Equal("player.hp", r.Divergence!.First.Path);
        Assert.Equal(1, r.Divergence.DecisionIndex);
        Assert.Equal(DivergenceClass.PortBug, r.Class, "a projection field mismatch is a port_bug candidate");
        Assert.SequenceEqual(new[] { 0 }, r.AgreedIndices);

        var unported = (JsonObject)bundle.DeepClone();
        Decision(unported, 0, 1)["pre_state"]!["enemies"]![0]!["monster_id"] = "SOME_UNPORTED_MONSTER";
        var u = Run(unported).Single();
        Assert.Equal(DivergenceClass.ContentNotPorted, u.Class, "an unported id at the divergent decision");

        var final = (JsonObject)bundle.DeepClone();
        final["segments"]![0]!["outcome"]!["final_hp"] = 999;
        var f = Run(final).Single();
        Assert.Equal(V2DivergenceKind.Final, f.Divergence!.Kind);

        var undo = (JsonObject)bundle.DeepClone();
        Decision(undo, 0, 0)["action"] = new JsonObject { ["type"] = "undo_end_turn" };
        Assert.Equal(DivergenceClass.OutOfScope, Run(undo).Single().Class);

        var unobserved = (JsonObject)bundle.DeepClone();
        foreach (var d in unobserved["segments"]![0]!["decisions"]!.AsArray())
        {
            d!["pre_state"] = null;
        }
        var un = Run(unobserved).Single();
        Assert.Equal(BundleStatus.Agree, un.Status);
        Assert.Equal(0, un.DecisionsCompared);
        Assert.Equal(un.DecisionsTotal, un.DecisionsUnobserved);

        var character = (JsonObject)bundle.DeepClone();
        character["character"] = "NOT_A_CHARACTER";
        var c = Run(character).Single();
        Assert.Equal(BundleStatus.Skipped, c.Status);
        Assert.Equal(DivergenceClass.ContentNotPorted, c.Class);

        var modifiers = (JsonObject)bundle.DeepClone();
        modifiers["modifiers"] = new JsonArray("DRAFT");
        Assert.Equal(DivergenceClass.OutOfScope, Run(modifiers).Single().Class);
    }

    public static void Test_DiffV2_RunSegmentsFromSeedAgreeAndChain()
    {
        var bundle = RecordRunSegments(25, 2, out var final);
        var segs = bundle["segments"]!.AsArray();
        var screens = segs.SelectMany(s => s!["decisions"]!.AsArray()).Select(d => d!["screen"]!.GetValue<string>()).Distinct().ToList();
        Assert.True(screens.Contains("map") && screens.Contains("combat") && screens.Contains("rewards"), "the recording covers map, combat and rewards: " + string.Join(",", screens));
        var results = Run(bundle);
        Assert.Equal(2, results.Count);
        Assert.Equal(BundleStatus.Agree, results[0].Status, results[0].Reason);
        Assert.Equal("seed", results[0].StartMode);
        Assert.Equal(BundleStatus.Agree, results[1].Status, results[1].Reason);
        Assert.Equal("chained", results[1].StartMode);
        Assert.True(results[1].FloorSummaryChecked && results[1].FloorSummaryAgreed, "floor summary compared (category prefixes stripped)");
        Assert.True(results[1].SoftDiffs.Any(s => s.Diff.Path == "floor_summary.player_stats"), "unprojected floor-summary fields are soft");
        Assert.True(final.State.MapPointHistory.Count >= 1);

        var gold = (JsonObject)bundle.DeepClone();
        int rewardsAt = segs[0]!["decisions"]!.AsArray().Select((d, i) => (d, i)).First(x => x.d!["screen"]!.GetValue<string>() == "rewards").i;
        Decision(gold, 0, rewardsAt)["pre_state"]!["player"]!["gold"] = 12345;
        var g = Run(gold);
        Assert.Equal(BundleStatus.HardDivergence, g[0].Status);
        Assert.Equal("player.gold", g[0].Divergence!.First.Path);
        Assert.Equal(BundleStatus.Skipped, g[1].Status, "no anchor after a divergence");
        Assert.True(g[1].Reason!.StartsWith("segment_after_unclassified_divergence", StringComparison.Ordinal));

        var gap = (JsonObject)bundle.DeepClone();
        Decision(gap, 0, 0)["pre_state"]!["player"]!["orbs"] = new JsonArray();
        Assert.Equal(DivergenceClass.ProjectionGap, Run(gap)[0].Class, "an oracle field the sim does not project");

        var badMove = (JsonObject)bundle.DeepClone();
        Decision(badMove, 0, 0)["action"] = new JsonObject { ["type"] = "map_move", ["col"] = 99, ["row"] = 99 };
        var bm = Run(badMove)[0];
        Assert.Equal("action.map_move", bm.Divergence!.First.Path);
        Assert.Equal(DivergenceClass.HarnessBug, bm.Class, "an observed state with an unmappable action");

        var room = (JsonObject)bundle.DeepClone();
        var travel = segs[0]!["decisions"]!.AsArray().Select((d, i) => (d, i)).First(x => x.d!["action"]!["type"]!.GetValue<string>() == "map_move").i;
        Decision(room, 0, travel)["room_entered"]!["model_id"] = "ENCOUNTER.SOMETHING_ELSE_WEAK";
        Assert.Equal(V2DivergenceKind.RoomEntry, Run(room)[0].Divergence!.Kind);

        var reward = (JsonObject)bundle.DeepClone();
        var items = Decision(reward, 0, rewardsAt)["pre_state"]!["rewards"]!["items"]!.AsArray();
        items.Add(new JsonObject { ["type"] = "potion", ["potion_id"] = "NOT_A_POTION", ["claimed"] = false });
        var rr = Run(reward)[0];
        Assert.Equal("rewards.items.length", rr.Divergence!.First.Path);
        Assert.Equal(DivergenceClass.ContentNotPorted, rr.Class);

        var screen = (JsonObject)bundle.DeepClone();
        Decision(screen, 0, 0)["screen"] = "shop";
        Assert.Equal(V2DivergenceKind.Screen, Run(screen)[0].Divergence!.Kind);

        var summary = (JsonObject)bundle.DeepClone();
        summary["segments"]![1]!["floor_summary"]!["map_point_type"] = "elite";
        Assert.Equal(V2DivergenceKind.FloorSummary, Run(summary)[1].Divergence!.Kind);
    }

    public static void Test_DiffV2_AnchorsResolveOrSkipWithClass()
    {
        var bundle = V2Shell();
        bundle["anchors"] = new JsonArray(
            new JsonObject { ["id"] = "a1", ["kind"] = "room_entry", ["ref"] = FixturePath() },
            new JsonObject { ["id"] = "a2", ["kind"] = "room_entry", ["ref"] = "native/saves/missing.save" },
            new JsonObject { ["id"] = "a3", ["kind"] = "room_entry", ["ref"] = FixturePath(), ["sha256"] = new string('0', 64) },
            new JsonObject { ["id"] = "m1", ["kind"] = "mcr_initial" });
        var combatFromMcr = CombatStart(StartingDecks.IroncladAtRunStart(0));
        bundle["segments"] = new JsonArray(
            new JsonObject { ["id"] = "s1", ["kind"] = "room", ["start_anchor"] = "a1", ["decisions"] = new JsonArray() },
            new JsonObject { ["id"] = "s2", ["kind"] = "room", ["start_anchor"] = "a2", ["decisions"] = new JsonArray() },
            new JsonObject { ["id"] = "s3", ["kind"] = "combat", ["start_anchor"] = "a3", ["decisions"] = new JsonArray() },
            new JsonObject { ["id"] = "s4", ["kind"] = "combat", ["start_anchor"] = "m1", ["start"] = combatFromMcr, ["decisions"] = new JsonArray() },
            new JsonObject { ["id"] = "s5", ["kind"] = "room", ["start_anchor"] = "m1", ["decisions"] = new JsonArray() });
        var results = Run(bundle);
        Assert.Equal(BundleStatus.Skipped, results[0].Status);
        Assert.True(results[0].Class is DivergenceClass.ContentNotPorted or DivergenceClass.RunLayerNotPorted, $"the fixture save imports with unported content or a run-layer stub: {results[0].Class} {results[0].Reason}");
        Assert.Equal(DivergenceClass.AnchorMissing, results[1].Class);
        Assert.Equal(DivergenceClass.AnchorError, results[2].Class);
        Assert.True(results[2].Reason!.StartsWith("anchor_mismatch", StringComparison.Ordinal));
        Assert.Equal(BundleStatus.Agree, results[3].Status, "an mcr_initial anchor with a v1-shaped start runs as v1: " + results[3].Reason);
        Assert.Equal("v1_start", results[3].StartMode);
        Assert.Equal(DivergenceClass.AnchorMissing, results[4].Class);
    }

    public static void Test_DiffV2_CoverageCountsTouchedAndAgreed()
    {
        var bundle = RecordCombatSegment(StartingDecks.IroncladAtRunStart(0));
        var second = (JsonObject)bundle.DeepClone();
        second["source"]!["lineage"] = "OTHER";
        var results = Run(bundle).Concat(Run(second)).ToList();
        var coverage = CoverageCounter.From(results);
        var strike = coverage.Areas["card"]["STRIKE_IRONCLAD"];
        Assert.True(strike.TouchedDecisions > 0 && strike.AgreedDecisions == strike.TouchedDecisions, "every touched decision agrees");
        Assert.Equal(2, strike.Lineages.Count);
        Assert.True(strike.MeetsAgreementMinimum);
        Assert.True(coverage.Areas["encounter"].ContainsKey(EncounterId));
        Assert.True(coverage.Areas["monster"].Count > 0);
        Assert.True(coverage.Areas["relic"].ContainsKey("BURNING_BLOOD"));
        var json = coverage.ToJson();
        Assert.True(json["areas"]!["card"]!["ids"]!["STRIKE_IRONCLAD"]!["ported"]!.GetValue<bool>());
    }

    public static void Test_DiffV2_BaselineRatchet()
    {
        var bundle = RecordCombatSegment(StartingDecks.IroncladAtRunStart(0));
        var good = Run(bundle);
        var baseline = Baseline.Build(good);
        var again = Baseline.Check(JsonNode.Parse(baseline.ToJsonString())!.AsObject(), Run(bundle));
        Assert.True(again.Passed, "an identical rerun passes the ratchet");
        Assert.True(again.DecisionsChecked == good[0].DecisionsAgreed && again.DecisionsChecked > 0);

        var broken = (JsonObject)bundle.DeepClone();
        Decision(broken, 0, 2)["pre_state"]!["player"]!["block"] = 77;
        var check = Baseline.Check(baseline, Run(broken));
        Assert.False(check.Passed);
        Assert.True(check.Violations.All(v => v.DecisionIndex >= 2), "only decisions from the divergence on regress");
        Assert.True(check.ChangedBundles.Count == 1, "the segment hash changed");

        var empty = Baseline.Check(baseline, new List<SegmentResult>());
        Assert.False(empty.Passed, "a vanished segment with agreed decisions is a violation");

        var newOnly = Baseline.Check(Baseline.Build(Run(broken)), Run(bundle));
        Assert.True(newOnly.Passed && newOnly.NewlyAgreeing.Count == 1, "a fixed segment is newly agreeing");
    }

    public static void Test_DiffV2_ReportGroupsByVersionKindAndClass()
    {
        var bundle = RecordCombatSegment(StartingDecks.IroncladAtRunStart(0));
        var hp = (JsonObject)bundle.DeepClone();
        Decision(hp, 0, 1)["pre_state"]!["player"]!["hp"] = 1;
        hp["source"]!["lineage"] = "HP";
        var silent = (JsonObject)bundle.DeepClone();
        silent["character"] = "NOT_A_CHARACTER";
        silent["source"]!["lineage"] = "NOT_A_CHARACTER";
        var results = Run(bundle).Concat(Run(hp)).Concat(Run(silent)).ToList();
        var report = ReportV2.Build(results, new List<IgnoredFile>(), new[] { "memory" }, new V2Options(), null, new SortedSet<string>());
        var combat = report["by_version"]![Version]!["by_segment_kind"]!["combat"]!;
        Assert.Equal(3, combat["segments"]!.GetValue<int>());
        Assert.Equal(1, combat["agree"]!.GetValue<int>());
        var classes = combat["by_first_divergence_class"]!.AsObject();
        Assert.Equal(1, classes["port_bug"]!["count"]!.GetValue<int>());
        Assert.Equal(1, classes["content_not_ported"]!["count"]!.GetValue<int>());
        Assert.Equal(1, report["divergences"]!.AsArray().Count);
        var div = report["divergences"]![0]!;
        Assert.Equal("player.hp", div["field_path"]!.GetValue<string>());
        Assert.Equal(1, div["actions_prefix"]!.AsArray().Count, "the repro carries the replayed prefix");
        Assert.NotNull(report["coverage"]!["areas"]);
    }

    public static void Test_DiffV2_CliRunsMixedDirectoryWithBaseline()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sts2sim-diffv2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bundle = RecordCombatSegment(StartingDecks.IroncladAtRunStart(0));
            File.WriteAllText(Path.Combine(dir, "v2.json"), bundle.ToJsonString());
            var sim = CombatSim.Create(RngSetup.Create(RngSetup.SeedingXxh64, Seed), StartingDecks.IroncladAtRunStart(0).Select(id => new CardSpec(id, false)).ToArray(), EncounterId, 0, 70, 80);
            var v1 = new DiffBundle
            {
                Source = new BundleSource("V1", null, null, 1, 2),
                GameVersion = "v0.107.1",
                Seeding = RngSetup.SeedingXxh64,
                Seed = Seed,
                EncounterId = EncounterId,
                PlayerHp = 70,
                PlayerMaxHp = 80,
                Deck = StartingDecks.IroncladAtRunStart(0).Select(id => new BundleCard(id, false)).ToList(),
                Decisions = new[] { new BundleDecision { Index = 0, PreState = SimProjector.Project(sim.State), Action = BundleAction.EndTurn } },
                Outcome = DiffBundle.OutcomeAbandoned
            };
            File.WriteAllText(Path.Combine(dir, "v1.json"), BundleJson.Write(v1).ToJsonString());
            File.WriteAllText(Path.Combine(dir, "reasons.json"), "{\"reasons\": {}}");
            string report = Path.Combine(dir, "out", "report.json");
            string baseline = Path.Combine(dir, "diff-baseline.json");
            int code = Program.Main(new[] { dir, "--v2", "--report", report, "--baseline", baseline });
            Assert.Equal(0, code);
            Assert.True(File.Exists(baseline), "a missing baseline is written");
            var rep = JsonNode.Parse(File.ReadAllText(report))!;
            Assert.Equal(2, rep["summary"]!["segments"]!.GetValue<int>());
            Assert.Equal(1, rep["ignored_files"]!.AsArray().Count);
            Assert.Equal(0, Program.Main(new[] { dir, "--baseline", baseline }), "the ratchet passes on a rerun");

            var broken = (JsonObject)bundle.DeepClone();
            Decision(broken, 0, 1)["pre_state"]!["player"]!["energy"] = 9;
            File.WriteAllText(Path.Combine(dir, "v2.json"), broken.ToJsonString());
            Assert.Equal(1, Program.Main(new[] { dir, "--v2", "--baseline", baseline, "--update-baseline" }));
            var kept = JsonNode.Parse(File.ReadAllText(baseline))!;
            Assert.True(kept["segments"]!.AsObject().Any(kv => kv.Value!["status"]!.GetValue<string>() == "agree" && kv.Key.EndsWith("/c1", StringComparison.Ordinal)), "a failing ratchet does not overwrite the baseline");
            Assert.Equal(2, Program.Main(new[] { dir, "--update-baseline" }), "--update-baseline needs --baseline");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
