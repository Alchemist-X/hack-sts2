using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static Sts2Recorder.Core.Tests.SessionTestHarness;

namespace Sts2Recorder.Core.Tests;

public sealed class DecisionCaptureTests
{
    private static DecisionCapture Capture(TrajectorySession session, string json, string stage = "before_input")
    {
        var state = JsonNode.Parse(json)!;
        return new(session, session.RecordState("decision:" + stage,
            state["state_type"]!.GetValue<string>(), state), stage);
    }

    [Fact]
    public void DelayedChoiceKeepsAllOffersAfterOverlayClosesAndNewSnapshotsArrive()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var choices = new DecisionChoices();
        var screen = new object();
        choices.Reserve(1, 7);
        Assert.True(choices.Bind(screen, 1));
        var before = Capture(session, """
            {"state_type":"card_reward","card_reward":{"cards":[
              {"id":"A","index":0},{"id":"B","index":1},{"id":"C","index":2}],
              "alternatives":[{"id":"Skip","choice_index":3}]}}
            """);
        choices.Update(screen, before);
        Capture(session, """{"state_type":"rewards","rewards":{"items":[]}}""");
        Capture(session, """{"state_type":"map","map":{"next_options":[]}}""");
        choices.Consume(1, 7)!.Record(session, "choice", "player_choice", new JsonObject { ["index"] = 1 });
        session.Flush();
        var action = Assert.Single(ReadJsonl(session.Directory, "actions"));
        Assert.Equal(before.StateSeq, action.GetProperty("state_seq").GetInt64());
        var state = ReadJsonl(session.Directory, "states").Single(s => s.GetProperty("seq").GetInt64() == before.StateSeq);
        Assert.Equal(3, state.GetProperty("state").GetProperty("card_reward").GetProperty("cards").GetArrayLength());
        Assert.Null(choices.Consume(1, 7)); // commitment cannot be emitted twice
        Assert.False(choices.Update(screen, before)); // late UI postfix cannot resurrect it
    }

    [Fact]
    public void RerollRebindsSameScreenToNewChoiceAndNewCandidateSet()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var choices = new DecisionChoices();
        var screen = new object();
        choices.Reserve(1, 10);
        choices.Bind(screen, 1);
        var first = Capture(session, """{"state_type":"card_reward","card_reward":{"cards":[{"id":"OLD"}]}}""");
        choices.Update(screen, first);
        Assert.Same(first, choices.Consume(1, 10));
        choices.Reserve(1, 11);
        choices.Bind(screen, 1);
        var second = Capture(session, """{"state_type":"card_reward","card_reward":{"cards":[{"id":"NEW"}]}}""");
        choices.Update(screen, second);
        Assert.Null(choices.Consume(1, 10));
        Assert.Same(second, choices.Consume(1, 11));
    }

    [Fact]
    public void UpgradeAndEnclosingRestChoiceRetainDifferentPreInputStates()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var rest = Capture(session, """
            {"state_type":"rest_site","player":{"hp":29,"max_hp":97},
             "rest_site":{"options":[{"id":"HEAL","is_enabled":true},{"id":"SMITH","is_enabled":true}]}}
            """);
        var choices = new DecisionChoices();
        var screen = new object();
        choices.Reserve(1, 5); choices.Bind(screen, 1);
        var upgrade = Capture(session, """
            {"state_type":"card_select","card_select":{"screen_type":"upgrade",
             "cards":[{"id":"GUARDS"},{"id":"GLOW"}],"preview_showing":false}}
            """);
        choices.Update(screen, upgrade);
        // Preview -> cancel -> choose a different card -> confirm. The confirmed
        // input replaces the earlier pin, but never the enclosing rest decision.
        var confirmed = Capture(session, """
            {"state_type":"card_select","card_select":{"screen_type":"upgrade",
             "cards":[{"id":"GUARDS","selected":true},{"id":"GLOW","selected":false}],
             "preview_showing":true,"can_confirm":true}}
            """);
        choices.Update(screen, confirmed);
        Capture(session, """{"state_type":"rest_site","rest_site":{"options":[],"can_proceed":true}}""");
        choices.Consume(1, 5)!.Record(session, "choice", "player_choice", new JsonObject());
        rest.Record(session, "rest", "rest_site_option", new JsonObject { ["option_id"] = "SMITH" });
        session.Flush();
        var actions = ReadJsonl(session.Directory, "actions");
        Assert.Equal(confirmed.StateSeq, actions[0].GetProperty("state_seq").GetInt64());
        Assert.Equal(rest.StateSeq, actions[1].GetProperty("state_seq").GetInt64());
    }

    [Theory]
    [InlineData("event", "event_option")]
    [InlineData("map", "vote_for_map_coord")]
    public void ClearingOptionsBeforeCommitDoesNotChangePinnedState(string screen, string kind)
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var before = Capture(session, $$$"""{"state_type":"{{{screen}}}","{{{screen}}}":{"options":[1,2,3]}}""");
        Capture(session, $$$"""{"state_type":"{{{screen}}}","{{{screen}}}":{"options":[]}}""");
        before.Record(session, "input", kind, new JsonObject());
        session.Flush();
        Assert.Equal(before.StateSeq, ReadJsonl(session.Directory, "actions")[0].GetProperty("state_seq").GetInt64());
    }

    [Fact]
    public void MissingCaptureDoesNotFallBackToLatestSnapshot()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        Capture(session, """{"state_type":"map"}""");
        new DecisionCapture(session, 0, "unbound_choice").Record(session, "input", "player_choice", new JsonObject());
        session.Flush();
        var action = Assert.Single(ReadJsonl(session.Directory, "actions"));
        Assert.Equal(JsonValueKind.Null, action.GetProperty("state_seq").ValueKind);
        Assert.Equal("missing", action.GetProperty("action").GetProperty("params").GetProperty("_decision").GetProperty("status").GetString());
    }

    [Fact]
    public void OldRunContinuationCannotWriteIntoNewSession()
    {
        using var oldSession = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        using var newSession = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var before = Capture(oldSession, """{"state_type":"rest_site"}""");
        Assert.Equal(0, before.Record(newSession, "rest", "rest_site_option", new JsonObject()));
        oldSession.Flush(); newSession.Flush();
        Assert.Empty(ReadJsonl(oldSession.Directory, "actions"));
        Assert.Empty(ReadJsonl(newSession.Directory, "actions"));
    }

    [Fact]
    public void NestedChoicesAndOtherPlayerReservationsCannotStealEachOthersCapture()
    {
        using var session = TrajectorySession.Begin(NewTempRoot(), DefaultMeta(), new FakeClock(StartTime));
        var choices = new DecisionChoices();
        var outer = new object(); var inner = new object();
        var a = Capture(session, """{"state_type":"card_select"}""");
        var b = Capture(session, """{"state_type":"hand_select"}""");
        choices.Reserve(1, 1); choices.Bind(outer, 1); choices.Update(outer, a);
        choices.Reserve(2, 99);
        choices.Reserve(1, 2); choices.Bind(inner, 1); choices.Update(inner, b);
        Assert.Same(b, choices.Consume(1, 2));
        Assert.Same(a, choices.Consume(1, 1));
        Assert.Null(choices.Consume(2, 99));
        Assert.False(choices.Bind(new object(), 1));
    }
}
