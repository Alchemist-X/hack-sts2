using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

/// <summary>
/// Runs the sandbox scenario and checks the session-format invariants the Python
/// validator enforces. When STS2REC_FAKE_SESSION_OUT is set (verify_core.sh), the
/// session is written there and persists for the Python cross-check.
/// </summary>
public sealed class FakeGameScenarioTests
{
    private static string ResolveOutputRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("STS2REC_FAKE_SESSION_OUT");
        return string.IsNullOrEmpty(fromEnv) ? NewTempRoot() : fromEnv;
    }

    [Fact]
    public void ScenarioProducesValidatableSession()
    {
        var sessionDir = FakeGameScenario.Run(ResolveOutputRoot());

        var states = ReadJsonl(sessionDir, "states");
        var actions = ReadJsonl(sessionDir, "actions");
        var events = ReadJsonl(sessionDir, "events");
        Assert.Equal(FakeGameScenario.ExpectedStates, states.Count);
        Assert.Equal(FakeGameScenario.ExpectedActions, actions.Count);
        Assert.Equal(FakeGameScenario.ExpectedEvents, events.Count);

        // session-global strictly monotonic, gapless seq across all three streams
        var seqs = AllSeqsInOrder(sessionDir);
        Assert.Equal(Enumerable.Range(1, seqs.Count).Select(n => (long)n), seqs);

        // no consecutive duplicate hashes (the validator rejects them)
        var hashes = states.Select(s => s.GetProperty("hash").GetString()).ToList();
        for (var i = 1; i < hashes.Count; i++)
        {
            Assert.NotEqual(hashes[i - 1], hashes[i]);
        }

        // every action references an existing snapshot strictly before itself
        var stateSeqs = states.Select(s => s.GetProperty("seq").GetInt64()).ToHashSet();
        foreach (var action in actions)
        {
            var stateSeq = action.GetProperty("state_seq").GetInt64();
            Assert.Contains(stateSeq, stateSeqs);
            Assert.True(stateSeq < action.GetProperty("seq").GetInt64());
        }

        // action status lifecycle present as scripted
        var statuses = actions.Select(a => a.GetProperty("status").GetString()).ToList();
        Assert.Equal(
            new[] { "cancelled", "executed", "executed", "committed", "committed" },
            statuses);

        // prelude: exactly the 5 combat-start draws precede the first action
        var firstActionSeq = actions.Min(a => a.GetProperty("seq").GetInt64());
        var preludeCount = events.Count(e => e.GetProperty("seq").GetInt64() < firstActionSeq);
        Assert.Equal(FakeGameScenario.ExpectedPreludeEvents, preludeCount);
        // and events exist after the final action too
        var lastActionSeq = actions.Max(a => a.GetProperty("seq").GetInt64());
        Assert.True(events.Count(e => e.GetProperty("seq").GetInt64() > lastActionSeq) >= 2);

        // finalized manifest with matching counts and both native artifacts
        var manifest = ReadManifest(sessionDir);
        Assert.False(manifest.GetProperty("incomplete").GetBoolean());
        Assert.True(manifest.GetProperty("result").GetProperty("win").GetBoolean());
        var counts = manifest.GetProperty("counts");
        Assert.Equal(states.Count, counts.GetProperty("states").GetInt64());
        Assert.Equal(actions.Count, counts.GetProperty("actions").GetInt64());
        Assert.Equal(events.Count, counts.GetProperty("events").GetInt64());
        Assert.True(File.Exists(Path.Combine(sessionDir, "native", "run_history.run")));
        Assert.True(File.Exists(Path.Combine(sessionDir, "native", "replay.mcr")));
    }
}
