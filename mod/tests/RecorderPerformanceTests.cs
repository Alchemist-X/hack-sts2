using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// Recorder perf instrumentation (manifest "perf" detail) and the hot-path
/// helpers it measures: the streaming state hash, the reflection metadata
/// cache, the bounded latency histogram, and decision-capture input
/// coalescing (one pre-input build per outer input, never per nested call).
/// </summary>
public sealed class RecorderPerformanceTests
{
    [Theory]
    [InlineData("{\"z\":[null,true,false,1,1.00,1e-8,-0.0],\"a\":{\"牌\":\"🔥<&>\\\"\",\"A\":42}}")]
    [InlineData("{\"state_type\":\"card_select\",\"cards\":[{\"index\":0,\"id\":\"A\"},{\"index\":1,\"id\":\"B\"}]}")]
    public void StreamingHashMatchesLegacyTreeCloneAndLeavesStateUntouched(string json)
    {
        var state = JsonNode.Parse(json)!;
        string original = state.ToJsonString();
        Assert.Equal(Hashing.HashFromCanonical(Hashing.CanonicalJson(state)), Hashing.HashState(state));
        Assert.Equal(original, state.ToJsonString()); // insertion order and values unchanged
    }

    [Fact]
    public void LongCandidateTailAndSelectionChangesAreNeverDeduplicatedAway()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var cards = new JsonArray(Enumerable.Range(0, 200).Select(i => (JsonNode)new JsonObject
            { ["id"] = "CARD." + i, ["choice_index"] = i, ["selected"] = false }).ToArray());
        var state = new JsonObject { ["state_type"] = "card_select", ["cards"] = cards };
        long before = session.RecordState("phase", "card_select", state, 1);
        Assert.Equal(before, session.RecordState("phase", "card_select", state, 1));
        cards[199]!["selected"] = true;
        long changed = session.RecordState("phase", "card_select", state, 1);
        Assert.NotEqual(before, changed);
        session.RecordAction("test", "player_choice", new JsonObject(), "committed", before);
        session.Flush();

        var snapshots = ReadJsonl(session.Directory, "states");
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, s => Assert.Equal(200, s.GetProperty("state").GetProperty("cards").GetArrayLength()));
        Assert.Equal(before, ReadJsonl(session.Directory, "actions")[0].GetProperty("state_seq").GetInt64());

        var perf = ReadManifest(session.Directory).GetProperty("perf");
        var build = perf.GetProperty("snapshot_build_ms");
        Assert.Equal(3, build.GetProperty("count").GetInt64()); // deduped builds still count
        Assert.Equal(1.0, build.GetProperty("p99_upper_bound").GetDouble(), precision: 6);
        Assert.Equal(3, perf.GetProperty("capture_by_trigger_ms").GetProperty("phase").GetProperty("count").GetInt64());
        Assert.Equal(1, perf.GetProperty("states_deduplicated").GetInt64());
        var states = perf.GetProperty("writer_queues").GetProperty("states");
        Assert.Equal(0, states.GetProperty("pending_bytes").GetInt64());
        Assert.True(states.GetProperty("peak_pending_lines").GetInt64() >= 1); // peaks survive Flush
        Assert.True(states.GetProperty("peak_pending_bytes").GetInt64() > 0);
    }

    [Fact]
    public void SnapshotCostKeepsLegacyCountersAndAddsDistributionAndTriggerSplit()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        session.RecordSnapshotCost(2, "phase");
        session.RecordSnapshotCost(4, "phase");
        session.RecordSnapshotCost(10, "poll");
        session.RecordSnapshotCost(1); // no trigger: legacy counters + distribution only
        session.Flush();

        var perf = ReadManifest(session.Directory).GetProperty("perf");
        var build = perf.GetProperty("snapshot_build_ms");
        Assert.Equal(4, build.GetProperty("count").GetInt64());
        Assert.Equal(4.25, build.GetProperty("avg").GetDouble(), precision: 3);
        Assert.Equal(10.0, build.GetProperty("max").GetDouble(), precision: 3);
        Assert.Equal(10.0, build.GetProperty("p99_upper_bound").GetDouble(), precision: 6);
        Assert.InRange(build.GetProperty("p95_upper_bound").GetDouble(), 4, 10);
        var byTrigger = perf.GetProperty("capture_by_trigger_ms");
        Assert.Equal(new[] { "phase", "poll" }, byTrigger.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(2, byTrigger.GetProperty("phase").GetProperty("count").GetInt64());
        Assert.Equal(4.0, byTrigger.GetProperty("phase").GetProperty("max").GetDouble(), precision: 6);
        Assert.Equal(10.0, byTrigger.GetProperty("poll").GetProperty("max").GetDouble(), precision: 6);
    }

    [Fact]
    public void TriggerSplitIsBoundedAndFoldsUnknownLabelsIntoOther()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        for (int i = 0; i < 40; i++)
        {
            session.RecordSnapshotCost(1, "trigger" + i);
        }
        session.RecordSnapshotCost(1, "trigger0"); // known label keeps its own bucket
        session.Flush();

        var byTrigger = ReadManifest(session.Directory).GetProperty("perf").GetProperty("capture_by_trigger_ms");
        Assert.Equal(33, byTrigger.EnumerateObject().Count());
        Assert.Equal(2, byTrigger.GetProperty("trigger0").GetProperty("count").GetInt64());
        Assert.Equal(8, byTrigger.GetProperty("other").GetProperty("count").GetInt64());
    }

    [Fact]
    public void DecisionCaptureKeepsItsPinAcrossLaterChangedSnapshots()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var cards = new JsonArray(Enumerable.Range(0, 200).Select(i => (JsonNode)new JsonObject
            { ["id"] = "CARD." + i, ["choice_index"] = i, ["selected"] = false }).ToArray());
        var state = new JsonObject { ["state_type"] = "card_select", ["cards"] = cards };
        long before = session.RecordState("decision:before_input", "card_select", state, 1);
        cards[199]!["selected"] = true;
        Assert.NotEqual(before, session.RecordState("decision:updated", "card_select", state, 1));
        new DecisionCapture(session, before, "before_input").Record(session, "test", "player_choice", new JsonObject());
        session.Flush();
        var action = ReadJsonl(session.Directory, "actions")[0];
        Assert.Equal(before, action.GetProperty("state_seq").GetInt64());
        Assert.Equal("captured", action.GetProperty("action").GetProperty("params")
            .GetProperty("_decision").GetProperty("status").GetString());
        var byTrigger = ReadManifest(session.Directory).GetProperty("perf").GetProperty("capture_by_trigger_ms");
        Assert.Equal(1, byTrigger.GetProperty("decision:before_input").GetProperty("count").GetInt64());
        Assert.Equal(1, byTrigger.GetProperty("decision:updated").GetProperty("count").GetInt64());
    }

    [Fact]
    public void NestedInputUsesOuterPreClickStateButNextClickCapturesAgain()
    {
        var choices = new DecisionChoices(); var screen = new object();
        choices.Reserve(1, 1); choices.Bind(screen, 1);
        using (var outer = choices.BeginInput(screen)!)
        {
            Assert.True(outer.ShouldCapture);
            using var inner = choices.BeginInput(screen)!;
            Assert.False(inner.ShouldCapture);
            Assert.True(choices.IsCurrent(screen, inner));
        }
        using var next = choices.BeginInput(screen)!;
        Assert.True(next.ShouldCapture);
    }

    [Fact]
    public void CoalescedCompletionRecordsFullOuterCandidatesAndOriginalPin()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var choices = new DecisionChoices(); var screen = new object();
        choices.Reserve(1, 1); choices.Bind(screen, 1);
        var state = new JsonObject { ["cards"] = new JsonArray("A", "B", "C") };
        int builds = 0;
        DecisionChoices.InputScope Enter()
        {
            var input = choices.BeginInput(screen)!;
            session.NoteDecisionInput(!input.ShouldCapture);
            if (input.ShouldCapture)
            {
                builds++;
                choices.Update(screen, new DecisionCapture(session,
                    session.RecordState("decision:before_input", "card_select", state, 0), "before_input"));
            }
            return input;
        }
        using (var outer = Enter())
        {
            state["cards"] = new JsonArray("C"); // UI has already changed inside click handler
            using var completion = Enter();
            session.RecordState("decision:updated", "card_select", state, 0);
            choices.Consume(1, 1)!.Record(session, "test", "player_choice", new JsonObject { ["index"] = 2 });
        }
        Assert.Equal(1, builds);
        session.Flush();
        var action = ReadJsonl(session.Directory, "actions")[0];
        var pinned = ReadJsonl(session.Directory, "states").Single(s =>
            s.GetProperty("seq").GetInt64() == action.GetProperty("state_seq").GetInt64());
        Assert.Equal(3, pinned.GetProperty("state").GetProperty("cards").GetArrayLength());
        var perf = ReadManifest(session.Directory).GetProperty("perf");
        Assert.Equal(1, perf.GetProperty("decision_input_scopes").GetInt64());
        Assert.Equal(1, perf.GetProperty("nested_inputs_coalesced").GetInt64());
    }

    [Fact]
    public void NestedOtherScreenAndRerollOnSameScreenNeverCoalesce()
    {
        var choices = new DecisionChoices(); var a = new object(); var b = new object();
        choices.Reserve(1, 1); choices.Bind(a, 1);
        using var outer = choices.BeginInput(a)!;
        choices.Reserve(1, 2); choices.Bind(b, 1);
        using var child = choices.BeginInput(b)!;
        Assert.True(child.ShouldCapture);
        choices.Consume(1, 1);
        choices.Reserve(1, 3); choices.Bind(a, 1);
        using var reroll = choices.BeginInput(a)!;
        Assert.True(reroll.ShouldCapture);
        Assert.False(choices.IsCurrent(a, outer));
        outer.Dispose(); // old finalizer cannot close the new choice's scope
        using var nestedReroll = choices.BeginInput(a)!;
        Assert.False(nestedReroll.ShouldCapture);
    }

    [Fact]
    public void FailedInputUnwindsAndDoesNotSuppressNextObservation()
    {
        var choices = new DecisionChoices(); var screen = new object();
        choices.Reserve(1, 1); choices.Bind(screen, 1);
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var input = choices.BeginInput(screen)!;
            throw new InvalidOperationException();
        }));
        using var next = choices.BeginInput(screen)!;
        Assert.True(next.ShouldCapture);
    }

    private class Parent
    {
        private string _candidate = "A";
        public void Reroll() => _candidate = "B";
        public string Value => _candidate;
    }
    private sealed class Child : Parent { }

    [Fact]
    public void FieldCacheReadsCurrentInheritedValuesAndCachesMissingFieldsSafely()
    {
        var model = new Child();
        Assert.Equal("A", FieldReaders.Read(model, "_candidate"));
        model.Reroll();
        Assert.Equal("B", FieldReaders.Read(model, "_candidate")); // metadata cached, value read afresh
        Assert.Null(FieldReaders.Read(model, "renamed"));
        Assert.Null(FieldReaders.Read(model, "renamed"));
    }

    [Fact]
    public void PercentileBoundsCoverActualPercentilesIncludingOverflow()
    {
        var histogram = new LatencyHistogram();
        for (int i = 1; i <= 100; i++) histogram.Add(i);
        var summary = histogram.Describe();
        Assert.InRange(summary["p95_upper_bound"]!.GetValue<double>(), 95, 100);
        Assert.InRange(summary["p99_upper_bound"]!.GetValue<double>(), 99, 100);
        histogram.Add(1e9);
        Assert.Equal(1e9, histogram.Describe()["max"]!.GetValue<double>());
    }
}
