using System.Globalization;
using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Play;

namespace Sts2Sim.Tests;

public static class PlayTests
{
    private const int Ascension = 10;

    private static (int Code, string Out, string Err) Cli(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        int code = PlayCli.Run(args, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static string NewTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sts2sim-play-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string[] Lines(string text) => text.TrimEnd('\n').Split('\n');

    private static string Hex(ulong hash) => hash.ToString("X16", CultureInfo.InvariantCulture);

    private static string OutcomeOf(CombatSim sim) => sim.Outcome switch
    {
        CombatOutcome.Victory => PlayGame.OutcomeVictory,
        CombatOutcome.Defeat => PlayGame.OutcomeDefeat,
        _ => PlayGame.OutcomeCap
    };

    private static void AssertLegalActionList(string printout, int expectedCount)
    {
        var lines = Lines(printout);
        int header = Array.FindIndex(lines, l => l.StartsWith("LEGAL ACTIONS ", StringComparison.Ordinal));
        Assert.True(header >= 0, "printout has a LEGAL ACTIONS header");
        Assert.Equal($"LEGAL ACTIONS {expectedCount} (reply with exactly one number):", lines[header]);
        Assert.Equal(header + expectedCount + 1, lines.Length, "the numbered list ends the printout");
        for (int k = 1; k <= expectedCount; k++)
        {
            Assert.True(lines[header + k].StartsWith($"  {k}. ", StringComparison.Ordinal), lines[header + k]);
        }
        Assert.Equal($"  {expectedCount}. End Turn", lines[^1]);
    }

    public static void Test_Play_SessionReplayMatchesDirectSimAndIsDeterministic()
    {
        string dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "agent.json");
            var created = Cli("new", "--session", path, "--seed", "102", "--encounter", "SLIMES_WEAK", "--ascension", "10");
            Assert.Equal(PlayCli.ExitOk, created.Code, created.Err);
            Assert.Equal("== COMBAT seed 102 | SLIMES_WEAK | A10 | turn 1 | phase Play | actions 0/100 | invalid 0", Lines(created.Out)[0]);

            var direct = CombatSim.Create(102, StartingDecks.Ironclad, "SLIMES_WEAK", Ascension);
            Assert.Equal(StartingDecks.IroncladStartingHp, direct.State.Player.CurrentHp);
            Assert.True(direct.State.Player.DrawPile.Cards.Concat(direct.State.Player.Hand.Cards).Any(c => c.Id == AscendersBane.CardId),
                "A10 starter deck carries Ascender's Bane");
            var buffer = new List<GameAction>();
            direct.LegalActions(buffer);
            AssertLegalActionList(created.Out, buffer.Count);

            var policy = new System.Random(20261030);
            int decisions = 0;
            string last = created.Out;
            while (!direct.IsTerminal && decisions < PlaySession.DefaultActionCap)
            {
                direct.LegalActions(buffer);
                int k = policy.Next(buffer.Count) + 1;
                var r = Cli("act", "--session", path, k.ToString(CultureInfo.InvariantCulture));
                Assert.Equal(PlayCli.ExitOk, r.Code, r.Err);
                Assert.True(r.Out.StartsWith($"APPLIED {k}: ", StringComparison.Ordinal), r.Out);
                direct.Step(buffer[k - 1]);
                decisions++;
                if (!direct.IsTerminal && decisions < PlaySession.DefaultActionCap)
                {
                    direct.LegalActions(buffer);
                    AssertLegalActionList(r.Out, buffer.Count);
                }
                last = r.Out;
            }
            Assert.True(decisions > 1, "the policy plays more than one decision");

            var session = PlaySession.Load(path);
            Assert.Equal(decisions, session.Actions.Count);
            Assert.Equal(0, session.InvalidAttempts);
            Assert.Equal(Hex(direct.StateHash()), session.StateHash);
            Assert.Equal(Hex(direct.StateHash()), PlayGame.Replay(PlaySession.Load(path)).StateHashHex);
            Assert.Equal(Hex(direct.StateHash()), PlayGame.Replay(PlaySession.Load(path)).StateHashHex, "second replay");

            string expected = new PlayStats(OutcomeOf(direct), StartingDecks.IroncladStartingHp, direct.State.Player.CurrentHp,
                direct.State.Player.TurnNumber, decisions, 0, 102, "SLIMES_WEAK", Ascension).ToJsonLine();
            var lastLines = Lines(last);
            Assert.Equal(StateRenderer.TerminalMarker, lastLines[^2]);
            Assert.Equal(expected, lastLines[^1]);
            Assert.Equal(expected + "\n", Cli("stats", "--session", path).Out);
            Assert.Equal(expected + "\n", Cli("stats", "--session", path).Out, "stats is a pure replay");
            Assert.Equal(Cli("show", "--session", path).Out, Cli("show", "--session", path).Out);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static void Test_Play_InvalidActionNumbersAreCountedAndChangeNothing()
    {
        string dir = NewTempDir();
        try
        {
            string path = Path.Combine(dir, "agent.json");
            Assert.Equal(PlayCli.ExitOk, Cli("new", "--session", path, "--seed", "101", "--encounter", "NIBBITS_WEAK", "--ascension", "10").Code);
            string hash0 = PlaySession.Load(path).StateHash!;
            string show0 = Cli("show", "--session", path).Out;
            int legal = PlayGame.Replay(PlaySession.Load(path)).LegalActionCount;
            var bad = new[]
            {
                new[] { "0" }, new[] { (legal + 1).ToString(CultureInfo.InvariantCulture) }, new[] { "-1" }, new[] { "abc" },
                new[] { "1.5" }, Array.Empty<string>(), new[] { "1", "2" }
            };
            int attempts = 0;
            foreach (var extra in bad)
            {
                var r = Cli(new[] { "act", "--session", path }.Concat(extra).ToArray());
                attempts++;
                Assert.Equal(PlayCli.ExitOk, r.Code, r.Err);
                Assert.True(r.Out.StartsWith("INVALID: ", StringComparison.Ordinal), r.Out);
                Assert.True(r.Out.Contains($"| invalid {attempts}\n", StringComparison.Ordinal), r.Out);
                AssertLegalActionList(r.Out, legal);
            }
            var session = PlaySession.Load(path);
            Assert.Equal(bad.Length, session.InvalidAttempts);
            Assert.Equal(0, session.Actions.Count);
            Assert.Equal(hash0, session.StateHash);
            Assert.Equal(hash0, PlayGame.Replay(session).StateHashHex);
            Assert.Equal(show0.Replace("| invalid 0\n", $"| invalid {bad.Length}\n", StringComparison.Ordinal), Cli("show", "--session", path).Out);
            Assert.Equal("{\"outcome\":\"in_progress\",\"hp_start\":80,\"hp_end\":80,\"turns\":1,\"decisions\":0,\"invalid_attempts\":7,\"seed\":101,\"encounter_id\":\"NIBBITS_WEAK\",\"ascension\":10}\n",
                Cli("stats", "--session", path).Out);

            var ok = Cli("act", "--session", path, "1");
            Assert.True(ok.Out.StartsWith("APPLIED 1: Play ", StringComparison.Ordinal), ok.Out);
            Assert.Equal(bad.Length, PlaySession.Load(path).InvalidAttempts);
            Assert.Equal(1, PlaySession.Load(path).Actions.Count);

            var usage = Cli("act", "--session", path, "--target", "1");
            Assert.Equal(PlayCli.ExitUsage, usage.Code);
            Assert.Equal(bad.Length, PlaySession.Load(path).InvalidAttempts, "usage errors are not invalid attempts");

            var again = Cli("new", "--session", path, "--seed", "101", "--encounter", "NIBBITS_WEAK", "--ascension", "10");
            Assert.Equal(PlayCli.ExitUsage, again.Code);
            Assert.Equal(1, PlaySession.Load(path).Actions.Count, "new never restarts an existing session");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static void Test_Play_RolloutReplaysAndCapAndTamperingAreHandled()
    {
        string dir = NewTempDir();
        try
        {
            string rolloutPath = Path.Combine(dir, "random.json");
            var r1 = Cli("rollout", "--seed", "104", "--encounter", "CORPSE_SLUGS_WEAK", "--ascension", "10", "--random", "--session", rolloutPath);
            Assert.Equal(PlayCli.ExitOk, r1.Code, r1.Err);
            var r2 = Cli("rollout", "--seed", "104", "--encounter", "CORPSE_SLUGS_WEAK", "--ascension", "10", "--random");
            Assert.Equal(r1.Out, r2.Out, "rollout is deterministic");
            Assert.Equal(r1.Out, Cli("stats", "--session", rolloutPath).Out);
            var rolled = PlaySession.Load(rolloutPath);
            Assert.Equal("random(policy_seed=104)", rolled.Policy);

            var direct = CombatSim.Create(104, StartingDecks.Ironclad, "CORPSE_SLUGS_WEAK", Ascension);
            var policy = new System.Random(PlayCli.DefaultPolicySeed(104));
            int steps = 0;
            while (!direct.IsTerminal && steps < PlaySession.DefaultActionCap)
            {
                var legal = direct.LegalActions();
                direct.Step(legal[policy.Next(legal.Count)]);
                steps++;
            }
            Assert.Equal(steps, rolled.Actions.Count);
            Assert.Equal(Hex(direct.StateHash()), rolled.StateHash);

            var capped = new PlaySession { Seed = 101, EncounterId = "NIBBITS_WEAK", Ascension = Ascension, ActionCap = 3 };
            var game = PlayGame.Replay(capped);
            for (int i = 0; i < 3; i++)
            {
                Assert.False(game.IsOver);
                var entry = game.Apply(game.LegalActionCount);
                Assert.Equal(GameAction.EndTurn, entry.Action);
            }
            Assert.False(game.Sim.IsTerminal, "precondition: three end turns do not finish the fight");
            Assert.True(game.IsOver);
            Assert.Equal(PlayGame.OutcomeCap, game.Outcome);
            string cappedPath = Path.Combine(dir, "capped.json");
            capped.Save(cappedPath);
            var after = Cli("act", "--session", cappedPath, "1");
            Assert.Equal(PlayCli.ExitOk, after.Code, after.Err);
            Assert.True(after.Out.StartsWith("INVALID: the combat is over", StringComparison.Ordinal), after.Out);
            var afterLines = Lines(after.Out);
            Assert.Equal(StateRenderer.TerminalMarker, afterLines[^2]);
            Assert.True(afterLines[^1].StartsWith("{\"outcome\":\"cap\",", StringComparison.Ordinal), afterLines[^1]);
            Assert.Equal(0, PlaySession.Load(cappedPath).InvalidAttempts, "acting after the end is not counted");
            Assert.Equal(3, PlaySession.Load(cappedPath).Actions.Count);

            var shifted = PlaySession.Load(rolloutPath);
            shifted.Actions[0] = shifted.Actions[0] with { K = shifted.Actions[0].K + 1 };
            string shiftedPath = Path.Combine(dir, "shifted.json");
            shifted.Save(shiftedPath);
            var shiftedShow = Cli("show", "--session", shiftedPath);
            Assert.Equal(PlayCli.ExitInternal, shiftedShow.Code);
            Assert.True(shiftedShow.Err.Contains("is not legal action", StringComparison.Ordinal), shiftedShow.Err);

            var rehashed = PlaySession.Load(rolloutPath);
            rehashed.StateHash = "0000000000000000";
            string rehashedPath = Path.Combine(dir, "rehashed.json");
            rehashed.Save(rehashedPath);
            var rehashedStats = Cli("stats", "--session", rehashedPath);
            Assert.Equal(PlayCli.ExitInternal, rehashedStats.Code);
            Assert.True(rehashedStats.Err.Contains("state hash", StringComparison.Ordinal), rehashedStats.Err);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
