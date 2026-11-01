namespace Sts2Sim.Server;

public enum GameKind
{
    Run,
    Combat
}

// One live game: either a full run (RunSim) or a standalone combat (CombatSim).
// Every action is carried as a RunAction; combat games convert with TryToCombat / FromCombat.
public sealed class GameSession
{
    private readonly List<RunAction> _runBuffer = new(64);
    private readonly List<GameAction> _combatBuffer = new(64);

    private GameSession(string id, GameKind kind, JsonNode? seed, JsonObject config)
    {
        Id = id;
        Kind = kind;
        Seed = seed;
        Config = config;
    }

    public string Id { get; }

    public GameKind Kind { get; }

    public JsonNode? Seed { get; }

    public JsonObject Config { get; }

    public RunSim? Run { get; private set; }

    public CombatSim? Combat { get; private set; }

    public List<string> Tokens { get; } = new();

    public bool Crashed { get; set; }

    public string? CrashError { get; set; }

    public string? CrashToken { get; set; }

    public object Gate { get; } = new();

    public static GameSession ForRun(string id, JsonNode? seed, JsonObject config, RunSim sim)
        => new(id, GameKind.Run, seed, config) { Run = sim };

    public static GameSession ForCombat(string id, JsonNode? seed, JsonObject config, CombatSim sim)
        => new(id, GameKind.Combat, seed, config) { Combat = sim };

    public bool IsTerminal => Run?.IsTerminal ?? Combat!.IsTerminal;

    public CombatState? LiveCombat
    {
        get
        {
            if (Combat != null)
            {
                return Combat.State;
            }
            var cs = Run!.State.Combat;
            return cs is { IsInProgress: true } ? cs : null;
        }
    }

    public PlayerState Player => Combat != null ? Combat.State.Player : Run!.State.Players[0];

    public ChoicePrompt? PendingChoice => Run != null ? Run.PendingChoice : Combat!.PendingChoice;

    public IReadOnlyList<int> PendingPicks => Run != null ? Run.PendingPicks : Combat!.PendingPicks;

    public string PhaseName
    {
        get
        {
            if (Run != null)
            {
                return Run.Phase.ToString();
            }
            if (!Combat!.IsTerminal)
            {
                return "Combat";
            }
            return Combat.Outcome == CombatOutcome.Victory ? "Victory" : "Defeat";
        }
    }

    public string? Result
    {
        get
        {
            if (!IsTerminal)
            {
                return null;
            }
            if (Run != null)
            {
                return Run.Phase == RunPhase.Victory ? "win" : "loss";
            }
            return Combat!.Outcome == CombatOutcome.Victory ? "win" : "loss";
        }
    }

    public int Steps => Tokens.Count;

    public List<RunAction> LegalActions()
    {
        var list = new List<RunAction>();
        if (IsTerminal)
        {
            return list;
        }
        if (Run != null)
        {
            Run.LegalActions(_runBuffer);
            list.AddRange(_runBuffer);
            return list;
        }
        Combat!.LegalActions(_combatBuffer);
        for (int i = 0; i < _combatBuffer.Count; i++)
        {
            list.Add(RunAction.FromCombat(_combatBuffer[i]));
        }
        return list;
    }

    public void Step(RunAction action)
    {
        if (Run != null)
        {
            Run.Step(action);
            return;
        }
        if (!action.TryToCombat(out var ga))
        {
            throw new InvalidOperationException($"{action} is not a combat action.");
        }
        Combat!.Step(ga);
    }

    public ulong StateHash() => Run != null ? Run.StateHash() : Combat!.StateHash();
}
