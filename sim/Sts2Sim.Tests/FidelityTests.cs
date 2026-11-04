using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;
using Sts2Sim.Diff.V2;

namespace Sts2Sim.Tests;

/// <summary>
/// Fidelity fixes from the E3 farm's open divergences (sim/specs/diff-report-e3-farm-2026-11-02.md §4), each from a
/// minimal repro of the recorded case.
/// </summary>
public static class FidelityTests
{
    // Leaves Neow with its first option and walks to the first monster room of the act; `beforeTravel` runs once on the
    // map, before the move into that room. Stops as soon as the combat exists (a start-of-turn prompt may be open).
    private static RunSim FirstCombat(string seed, string character, Action<RunSim>? beforeTravel = null)
    {
        var sim = RunSim.NewRun(new RunConfig(seed, character, 0));
        for (int guard = 0; guard < 64 && sim.State.Combat == null; guard++)
        {
            var legal = sim.LegalActions();
            var pick = legal.FirstOrDefault(a => a.Kind == RunActionKind.Travel && TravelIsCombat(sim, a.A));
            if (pick != default)
            {
                beforeTravel?.Invoke(sim);
                beforeTravel = null;
            }
            else
            {
                pick = legal.FirstOrDefault(a => a.Kind is RunActionKind.ChooseEventOption or RunActionKind.LeaveEvent or RunActionKind.ProceedRewards
                    or RunActionKind.PromptSelect or RunActionKind.PromptConfirm);
            }
            if (pick == default)
            {
                pick = legal[0];
            }
            sim.Step(pick);
        }
        Assert.NotNull(sim.State.Combat);
        return sim;
    }

    private static bool TravelIsCombat(RunSim sim, int index)
    {
        var points = new List<MapPoint>();
        RunLegalActionGenerator.GetLegalMapPoints(sim.State, points);
        return index >= 0 && index < points.Count && points[index].PointType == MapPointType.Monster;
    }

    public static void Test_Fidelity_TurnsTakenIsTheTurnNumberWhenTheCombatEnds()
    {
        // Combat/CombatManager.cs:1650 captures TurnNumber at the start of EndCombatInternal and writes it to the history
        // room (:1737-1739). A win at the end of the enemy turn (Doom, Models.Powers/DoomPower.cs BeforeSideTurnEnd) is
        // followed by SwitchSides in EndEnemyTurn (:1950-1976; IsCombatEnding is false once IsInProgress is false,
        // :6550-6553), which bumps TurnNumber. Farm SDR1TFEZ2U f15: the game's turns_taken 4, the sim's 5.
        var sim = FirstCombat("FIDTURNS01", "IRONCLAD");
        var cs = sim.State.Combat!;
        foreach (var monster in cs.Monsters.ToArray())
        {
            PowerCmd.Apply<DoomPower>(cs, monster, monster.CurrentHp, cs.Player, null);
        }
        Assert.Equal(1, cs.Player.TurnNumber);
        sim.Step(RunAction.Of(RunActionKind.EndTurn));
        Assert.True(sim.Phase != RunPhase.Combat, "every enemy dies to Doom at the end of its turn");
        Assert.Equal(1, cs.TurnsTakenAtCombatEnd, "captured when EndCombatInternal started");
        Assert.Equal(2, cs.Player.TurnNumber, "SwitchSides still ran after the win, as in the game");
        Assert.Equal(1, sim.State.MapPointHistory[^1].Rooms[^1].TurnsTaken);
    }

    public static void Test_Fidelity_TurnPhaseIsProjectedAtTheRecordersResolution()
    {
        // The recorder keeps the side and `PlayerTurnPhase == Play` only (mod/src/StateBuilder/PassiveStateBuilder.Combat.cs:37,
        // :40; sts2rec/sts2rec/diff_bundle.py:574-581). GAMBLING_CHIP prompts in AfterPlayerTurnStart on turn 1
        // (Models.Relics/GamblingChip.cs), i.e. in PlayerTurnPhase.Start (Combat/CombatManager.cs:5293 before :3902, :3914).
        // Farm UF51XE6ZV1 f11: oracle `player_not_play`, sim `player_start`.
        var sim = FirstCombat("FIDCHIP01", "IRONCLAD", s => RelicCmd.Obtain(s.State, GamblingChip.RelicId, s.State.Players[0]));
        var cs = sim.State.Combat!;
        var prompt = Assert.NotNull(sim.PendingChoice);
        Assert.Equal("GAMBLING_CHIP:AfterPlayerTurnStart", prompt.SiteId);
        Assert.Equal(PlayerTurnPhase.Start, cs.Player.Phase);
        Assert.Equal(ProjectionJson.PhasePlayerNotPlay, SimProjector.Project(cs).Turn.Phase);
        cs.Player.Phase = PlayerTurnPhase.Play;
        Assert.Equal(ProjectionJson.PhasePlayerPlay, SimProjector.Phase(cs));
        cs.Player.Phase = PlayerTurnPhase.AutoPostPlay;
        Assert.Equal("player_not_play", SimProjector.Phase(cs));
        cs.CurrentSide = CombatSide.Enemy;
        Assert.Equal("enemy_turn", SimProjector.Phase(cs), "the exporter's `<side>_turn`");
    }

    public static void Test_Fidelity_DrawPileGridsCompareAsAMultisetAndPickByIdentity()
    {
        // STRATAGEM_POWER AfterShuffle (Models.Powers/StratagemPower.cs) selects from the draw pile. The prompt lists it by
        // rarity, then ModelId (Commands/CardSelectCmd.cs:897); the recorder reads the screen, which sorts by rarity, then
        // the localized title (Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:412-430, Nodes.Cards/NCardGrid.cs:619).
        // Farm 3ZGJFKTD64 f9 (zh-CN game): same 15 cards, other order; the agent picked CRUSH_UNDER at UI index 8.
        string[] ui = { "STRIKE_REGENT", "STRIKE_REGENT", "DEFEND_REGENT", "DEFEND_REGENT+", "FALLING_STAR", "GATHER_LIGHT", "CRUSH_UNDER", "ASTRAL_PULSE" };
        string[] rules = { "DEFEND_REGENT", "DEFEND_REGENT+", "FALLING_STAR", "STRIKE_REGENT", "STRIKE_REGENT", "ASTRAL_PULSE", "CRUSH_UNDER", "GATHER_LIGHT" };
        JsonObject Card(string key) => new() { ["id"] = key.TrimEnd('+'), ["upgraded"] = key.EndsWith('+') };
        var oracle = new JsonObject { ["kind"] = "card_select", ["candidates"] = new JsonArray(ui.Select(k => (JsonNode)Card(k)).ToArray()) };
        var options = rules.Select((k, i) => new ChoiceOption(100 + i, k.TrimEnd('+'), k.EndsWith('+') ? 1 : 0)).ToList();
        var prompt = new ChoicePrompt(ChoicePromptKind.PileSelect, "STRATAGEM_POWER:AfterShuffle", "STRATAGEM_POWER", 1, PileType.Draw, options,
            1, 1, false, false, false, false, ChoiceResultType.CombatCard);
        var sim = RunScreenProjector.Select(prompt, Array.Empty<int>());
        Assert.Equal("draw", sim["pile"]!.GetValue<string>());
        var diffs = V2Runner.CompareCombatSelect(oracle, sim);
        Assert.Equal(0, diffs.Count(d => d.Severity == DiffSeverity.Hard), string.Join("; ", diffs.Select(d => d.Path)));
        var other = (JsonObject)oracle.DeepClone();
        other["candidates"]![0]!["upgraded"] = true;
        Assert.True(V2Runner.CompareCombatSelect(other, sim).Any(d => d.Severity == DiffSeverity.Hard), "a different multiset still diverges");
        var discard = prompt with { Pile = PileType.Discard };
        Assert.True(V2Runner.CompareCombatSelect(oracle, RunScreenProjector.Select(discard, Array.Empty<int>())).Any(d => d.Severity == DiffSeverity.Hard),
            "other piles keep their order (SortingOrders.Ascending, NCombatPileCardSelectScreen.cs:412-419)");
        var choose = new JsonObject { ["type"] = "choose", ["kind"] = "grid", ["picks"] = new JsonArray(new JsonObject { ["index"] = 5, ["card_id"] = "GATHER_LIGHT", ["upgraded"] = false }) };
        var action = new V2Action(V2Action.Choose, choose, new[] { new ChoosePick(5, null, null, "GATHER_LIGHT", false) });
        var picks = ActionMapper.PicksByIdentity(action, prompt);
        Assert.Null(picks[0].Index);
        Assert.Equal(7, ActionMapper.ResolvePick(prompt, Array.Empty<int>(), picks[0], out var miss), "UI index 5 is the prompt's option 7");
        Assert.Null(miss);
        Assert.Equal(5, ActionMapper.PicksByIdentity(action, discard)[0].Index, "non-draw grids keep the recorded index");
    }
}

