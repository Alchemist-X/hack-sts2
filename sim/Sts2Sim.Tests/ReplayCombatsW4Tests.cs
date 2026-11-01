using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;
using Sts2Sim.Diff.V2;

namespace Sts2Sim.Tests;

public static class ReplayCombatsW4Tests
{
    private const string Version = "v0.111.0";

    private static JsonObject Shell(string seed, string gameMode = "standard", JsonArray? runReasons = null, bool withSeeding = true)
    {
        var shell = new JsonObject
        {
            ["bundle_version"] = 2,
            ["kind"] = "run",
            ["source"] = new JsonObject { ["lineage"] = "W4-" + seed },
            ["game_version"] = Version,
            ["rules_version"] = Version,
            ["seed"] = seed,
            ["character"] = StartingDecks.IroncladCharacterId,
            ["ascension"] = 0,
            ["game_mode"] = gameMode,
            ["modifiers"] = new JsonArray(),
            ["anchors"] = new JsonArray(),
            ["segments"] = new JsonArray(),
            ["eligibility"] = new JsonObject { ["run_reasons"] = runReasons ?? new JsonArray() }
        };
        if (withSeeding)
        {
            shell["seeding"] = "xxh64";
        }
        return shell;
    }

    private static JsonObject CombatSegment(string id, JsonNode? deck, JsonNode? action, bool eligible, params string[] reasons)
    {
        var reasonArray = new JsonArray();
        foreach (var r in reasons)
        {
            reasonArray.Add(r);
        }
        return new JsonObject
        {
            ["id"] = id,
            ["kind"] = "combat",
            ["act"] = 1,
            ["floor"] = 2,
            ["room_type"] = "monster",
            ["room_model_id"] = "ENCOUNTER.NIBBITS_WEAK",
            ["attempt"] = 1,
            ["start_anchor"] = null,
            ["start"] = new JsonObject
            {
                ["encounter_id"] = "NIBBITS_WEAK",
                ["player"] = new JsonObject { ["hp"] = 70, ["max_hp"] = 80 },
                ["deck"] = deck,
                ["relics"] = new JsonArray("BURNING_BLOOD"),
                ["rng_offsets"] = new JsonObject()
            },
            ["decisions"] = new JsonArray(new JsonObject { ["index"] = 0, ["screen"] = "combat", ["pre_state"] = null, ["action"] = action }),
            ["outcome"] = new JsonObject { ["result"] = "win" },
            ["eligibility"] = new JsonObject { ["eligible"] = eligible, ["reasons"] = reasonArray }
        };
    }

    private static JsonArray StarterDeck()
    {
        var deck = new JsonArray();
        foreach (var id in StartingDecks.Ironclad)
        {
            deck.Add(new JsonObject { ["id"] = id, ["upgraded"] = false });
        }
        return deck;
    }

    private static List<SegmentResult> Run(JsonObject bundle)
        => V2Runner.RunBundle(RunBundleJson.Parse(bundle.ToJsonString(), "memory://w4.json"), new V2Options { IncludeIneligible = true });

    public static void Test_ReplayCombatsW4_ExporterNullsStayPerSegment()
    {
        var bundle = Shell("W4NULLS0001");
        var segments = bundle["segments"]!.AsArray();
        segments.Add(CombatSegment("s1", null, new JsonObject { ["type"] = "end_turn" }, false, "recorder_gap:deck_order", "anchor_missing"));
        segments.Add(CombatSegment("s2", StarterDeck(), null, false, "decision_unresolved"));
        var results = Run(bundle);
        Assert.Equal(2, results.Count);
        Assert.Equal(BundleStatus.Skipped, results[0].Status);
        Assert.Equal(DivergenceClass.ExporterIneligible, results[0].Class);
        Assert.True(results[0].Reason!.Contains("'deck'", StringComparison.Ordinal), results[0].Reason);
        Assert.Equal(BundleStatus.Skipped, results[1].Status);
        Assert.Equal(DivergenceClass.ExporterIneligible, results[1].Class);
        Assert.True(results[1].Reason!.Contains("'action'", StringComparison.Ordinal), results[1].Reason);
        segments.Add(CombatSegment("s3", StarterDeck(), null, true));
        bool threw = false;
        try
        {
            RunBundleJson.Parse(bundle.ToJsonString(), "memory://w4-eligible.json");
        }
        catch (BundleFormatException ex)
        {
            threw = ex.Path.Contains("$.segments[2]", StringComparison.Ordinal);
        }
        Assert.True(threw, "an eligible malformed segment still fails the bundle at its path");
    }

    public static void Test_ReplayCombatsW4_OldPrefixRunWithoutSeedingReads()
    {
        var bundle = Shell("oldG0G2JMFEYHRS", withSeeding: false, runReasons: new JsonArray("seed_old_prefix"));
        bundle["segments"]!.AsArray().Add(CombatSegment("s1", StarterDeck(), new JsonObject { ["type"] = "end_turn" }, false, "seed_old_prefix"));
        var run = RunBundleJson.Parse(bundle.ToJsonString(), "memory://w4-old.json");
        Assert.Equal(RunBundleJson.UnknownSeeding, run.Seeding);
        var results = V2Runner.RunBundle(run, new V2Options { IncludeIneligible = true });
        Assert.Equal(1, results.Count);
        Assert.Equal(BundleStatus.Skipped, results[0].Status);
        Assert.Equal(DivergenceClass.VersionDrift, results[0].Class);
        var fresh = Shell("G0G2JMFEYHRS", withSeeding: false);
        bool threw = false;
        try
        {
            RunBundleJson.Parse(fresh.ToJsonString(), "memory://w4-noseed.json");
        }
        catch (BundleFormatException)
        {
            threw = true;
        }
        Assert.True(threw, "a run without the 'old' prefix still needs 'seeding'");
    }

    public static void Test_ReplayCombatsW4_CustomModeWithEmptyModifiersPassesRunGate()
    {
        var custom = RunBundleJson.Parse(Shell("G0G2JMFEYHRS", gameMode: "custom").ToJsonString(), "memory://w4-custom.json");
        Assert.Null(V2Runner.RunGate(custom));
        var unknown = RunBundleJson.Parse(Shell("G0G2JMFEYHRS", gameMode: "custom", runReasons: new JsonArray("game_mode_modifiers_unknown:custom")).ToJsonString(), "memory://w4-unknown.json");
        Assert.True(V2Runner.RunGate(unknown) != null, "custom with unverified modifiers stays out of scope");
        var withModifiers = Shell("G0G2JMFEYHRS", gameMode: "custom");
        withModifiers["modifiers"] = new JsonArray("DRAFT");
        Assert.True(V2Runner.RunGate(RunBundleJson.Parse(withModifiers.ToJsonString(), "memory://w4-mod.json")) != null, "modifiers stay out of scope");
        var daily = RunBundleJson.Parse(Shell("G0G2JMFEYHRS", gameMode: "daily").ToJsonString(), "memory://w4-daily.json");
        Assert.True(V2Runner.RunGate(daily) != null, "other modes stay out of scope");
    }

    public static void Test_ReplayCombatsW4_FromSetUpMatchesCreate()
    {
        var rng = new RunRngSet("G0G2JMFEYHRS");
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        var context = CombatRunContext.ForAct(1, 2);
        var created = CombatSim.Create(rng, deck, "NIBBITS_WEAK", 0, 70, 80, context);
        var fromSetUp = CombatSim.FromSetUp(CombatLoop.SetUpCombat(rng.DeepClone(), deck, "NIBBITS_WEAK", 0, 70, 80, context));
        Assert.Equal(StateHasher.Hash(created.State), StateHasher.Hash(fromSetUp.State));
        bool threw = false;
        try
        {
            CombatSim.FromSetUp(fromSetUp.State);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.True(threw, "a started combat is rejected");
    }
}
