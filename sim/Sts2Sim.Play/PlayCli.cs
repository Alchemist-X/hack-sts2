namespace Sts2Sim.Play;

public static class PlayCli
{
    public const int ExitOk = 0;

    public const int ExitInternal = 1;

    public const int ExitUsage = 2;

    public const int MaxAscension = (int)AscensionLevel.DoubleBoss;

    public static readonly string Usage =
        "usage (always run the prebuilt dll: dotnet <path>/Sts2Sim.Play.dll ...):\n" +
        "  new     --session FILE --seed N --encounter ID --ascension A [--hp H] [--max-hp M]\n" +
        "  act     --session FILE K          apply legal action number K from the latest printout\n" +
        "  show    --session FILE            reprint the current state (no action taken)\n" +
        "  stats   --session FILE            one JSON line: outcome, hp_start, hp_end, turns, decisions, invalid_attempts\n" +
        "  rollout --seed N --encounter ID --ascension A --random [--session FILE] [--policy-seed S] [--hp H] [--max-hp M]\n" +
        $"Deck: Ironclad starter (Ascender's Bane added at A5+). Cap: {PlaySession.DefaultActionCap} actions per combat (outcome cap).\n";

    private static readonly HashSet<string> SessionOnlyOptions = new(StringComparer.Ordinal) { "--session" };

    private static readonly HashSet<string> NewOptions = new(StringComparer.Ordinal)
    {
        "--session", "--seed", "--encounter", "--ascension", "--hp", "--max-hp"
    };

    private static readonly HashSet<string> RolloutOptions = new(StringComparer.Ordinal)
    {
        "--session", "--seed", "--encounter", "--ascension", "--hp", "--max-hp", "--random", "--policy-seed"
    };

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            stderr.Write(Usage);
            return ExitUsage;
        }
        if (args[0] is "-h" or "--help" or "help")
        {
            stdout.Write(Usage);
            return ExitOk;
        }
        try
        {
            return args[0] switch
            {
                "new" => New(Options.Parse(args, NewOptions), stdout),
                "act" => Act(Options.Parse(args, SessionOnlyOptions, positionals: true), stdout),
                "show" => Show(Options.Parse(args, SessionOnlyOptions), stdout),
                "stats" => Stats(Options.Parse(args, SessionOnlyOptions), stdout),
                "rollout" => Rollout(Options.Parse(args, RolloutOptions), stdout),
                _ => throw new UsageException($"unknown command '{args[0]}'")
            };
        }
        catch (PlayException ex)
        {
            stderr.WriteLine("error: " + ex.Message);
            if (ex is UsageException)
            {
                stderr.Write(Usage);
            }
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine("error: " + ex.Message);
            return ExitUsage;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"internal error {ex.GetType().Name}: {ex.Message}");
            return ExitInternal;
        }
    }

    private static int New(Options o, TextWriter stdout)
    {
        string path = o.Require("--session");
        var session = SessionFromOptions(o, PlaySession.AgentPolicy);
        if (File.Exists(path))
        {
            throw new UsageException($"session file already exists: {path} (an existing combat is never restarted; use act/show on it, or delete the file deliberately)");
        }
        var game = PlayGame.Replay(session);
        session.StateHash = game.StateHashHex;
        session.Save(path);
        stdout.Write(StateRenderer.Render(game));
        return ExitOk;
    }

    private static int Act(Options o, TextWriter stdout)
    {
        string path = o.Require("--session");
        var session = PlaySession.Load(path);
        var game = PlayGame.Replay(session);
        var sb = new StringBuilder(2048);
        if (game.IsOver)
        {
            sb.Append("INVALID: the combat is over; no more actions are accepted.\n");
            StateRenderer.AppendState(sb, game);
            stdout.Write(sb.ToString());
            return ExitOk;
        }
        int count = game.LegalActionCount;
        if (!TryParseActionNumber(o.Positionals, count, out int k))
        {
            session.InvalidAttempts++;
            session.Save(path);
            string problem = o.Positionals.Count == 0
                ? "no action number given"
                : $"\"{string.Join(" ", o.Positionals)}\" is not a legal action number";
            sb.Append($"INVALID: {problem}; reply with exactly one number from 1 to {count}. (invalid attempts so far: {session.InvalidAttempts})\n");
            StateRenderer.AppendState(sb, game);
            stdout.Write(sb.ToString());
            return ExitOk;
        }
        var cs = game.Sim.State;
        var before = StateRenderer.Snapshot(cs);
        string text = StateRenderer.ActionText(cs, game.Sim.LegalActions()[k - 1]);
        game.Apply(k);
        session.Save(path);
        sb.Append($"APPLIED {k}: {text}\n");
        sb.Append($"CHANGES: {StateRenderer.Changes(before, StateRenderer.Snapshot(game.Sim.State))}\n");
        StateRenderer.AppendState(sb, game);
        stdout.Write(sb.ToString());
        return ExitOk;
    }

    private static int Show(Options o, TextWriter stdout)
    {
        var game = PlayGame.Replay(PlaySession.Load(o.Require("--session")));
        stdout.Write(StateRenderer.Render(game));
        return ExitOk;
    }

    private static int Stats(Options o, TextWriter stdout)
    {
        var game = PlayGame.Replay(PlaySession.Load(o.Require("--session")));
        stdout.Write(game.Stats().ToJsonLine() + "\n");
        return ExitOk;
    }

    private static int Rollout(Options o, TextWriter stdout)
    {
        if (!o.HasFlag("--random"))
        {
            throw new UsageException("rollout needs --random (uniform random choice over the legal actions)");
        }
        ulong seed = o.RequireUInt64("--seed");
        int policySeed = o.OptionalInt("--policy-seed") ?? DefaultPolicySeed(seed);
        string? path = o.Optional("--session");
        if (path != null && File.Exists(path))
        {
            throw new UsageException($"session file already exists: {path}");
        }
        var session = SessionFromOptions(o, $"random(policy_seed={policySeed.ToString(CultureInfo.InvariantCulture)})");
        var game = PlayRandom(session, policySeed);
        if (path != null)
        {
            session.Save(path);
        }
        stdout.Write(game.Stats().ToJsonLine() + "\n");
        return ExitOk;
    }

    public static int DefaultPolicySeed(ulong seed) => (int)(seed % int.MaxValue);

    public static PlayGame PlayRandom(PlaySession session, int policySeed)
    {
        var game = PlayGame.Replay(session);
        var policy = new System.Random(policySeed);
        while (!game.IsOver)
        {
            game.Apply(policy.Next(game.LegalActionCount) + 1);
        }
        session.StateHash = game.StateHashHex;
        return game;
    }

    public static bool TryParseActionNumber(IReadOnlyList<string> positionals, int legalCount, out int k)
    {
        k = 0;
        return positionals.Count == 1
               && int.TryParse(positionals[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out k)
               && k >= 1
               && k <= legalCount;
    }

    private static PlaySession SessionFromOptions(Options o, string policy)
    {
        ulong seed = o.RequireUInt64("--seed");
        string encounter = o.Require("--encounter");
        if (!EncounterRegistry.TryGet(encounter, out _))
        {
            throw new UsageException($"unknown encounter '{encounter}'; registered: {string.Join(", ", EncounterRegistry.AllIds)}");
        }
        int ascension = o.RequireInt("--ascension");
        if (ascension < 0 || ascension > MaxAscension)
        {
            throw new UsageException($"--ascension must be 0..{MaxAscension} (got {ascension})");
        }
        return new PlaySession
        {
            Seed = seed,
            EncounterId = encounter,
            Ascension = ascension,
            CurrentHp = o.OptionalInt("--hp"),
            MaxHp = o.OptionalInt("--max-hp"),
            Policy = policy
        };
    }

    private sealed class Options
    {
        private static readonly HashSet<string> Flags = new(StringComparer.Ordinal) { "--random" };

        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

        public List<string> Positionals { get; } = new();

        public static Options Parse(string[] args, HashSet<string> allowed, bool positionals = false)
        {
            var o = new Options();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--", StringComparison.Ordinal))
                {
                    if (!allowed.Contains(a))
                    {
                        throw new UsageException($"option {a} is not valid for '{args[0]}'");
                    }
                    if (Flags.Contains(a))
                    {
                        o._flags.Add(a);
                        continue;
                    }
                    if (i + 1 >= args.Length)
                    {
                        throw new UsageException($"option {a} needs a value");
                    }
                    if (!o._values.TryAdd(a, args[++i]))
                    {
                        throw new UsageException($"option {a} is given twice");
                    }
                }
                else if (positionals)
                {
                    o.Positionals.Add(a);
                }
                else
                {
                    throw new UsageException($"unexpected argument '{a}' for '{args[0]}'");
                }
            }
            return o;
        }

        public bool HasFlag(string name) => _flags.Contains(name);

        public string? Optional(string name) => _values.TryGetValue(name, out var v) ? v : null;

        public string Require(string name) => Optional(name) ?? throw new UsageException($"missing {name}");

        public int RequireInt(string name) => ParseInt(name, Require(name));

        public int? OptionalInt(string name) => Optional(name) is { } v ? ParseInt(name, v) : null;

        public ulong RequireUInt64(string name)
        {
            string v = Require(name);
            return ulong.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out ulong result)
                ? result
                : throw new UsageException($"{name} must be a non-negative integer (got '{v}')");
        }

        private static int ParseInt(string name, string v)
            => int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int result)
                ? result
                : throw new UsageException($"{name} must be an integer (got '{v}')");
    }
}
