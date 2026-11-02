using System.Diagnostics;
using System.Globalization;

namespace Sts2Sim.Server;

// Parsed command line of Sts2Sim.Server (Program.ParseArgs).
public sealed class ServerArgs
{
    public int Port { get; set; } = 8765;
    public string Bind { get; set; } = HttpHost.DefaultBind;
    public string? CrashLog { get; set; } = "tmp/play-crashes.jsonl";
    public int? SelfTest { get; set; }
    public string? Replay { get; set; }
    public int? Line { get; set; }
    public int Seed { get; set; } = 1;
    public int Ascension { get; set; }
    public int StepCap { get; set; } = 6_000;
    public string Content { get; set; } = "ported_only";
    public string Policy { get; set; } = "random";
    public string? Knowledge { get; set; }
    public bool RevealMoves { get; set; }
    public bool Help { get; set; }
    public List<string> Characters { get; } = new();
}

public static class Program
{
    private const string Usage =
        "usage: Sts2Sim.Server [--port 8765] [--bind 127.0.0.1] [--crash-log tmp/play-crashes.jsonl | --no-crash-log] [--knowledge DIR] [--reveal-moves]\n" +
        "       Sts2Sim.Server --selftest N [--seed 1] [--character IRONCLAD] [--ascension 0] [--content ported_only|all] [--step-cap 6000] [--policy random|biased]\n" +
        "       Sts2Sim.Server --replay tmp/play-crashes.jsonl [--line N]   (N is 1-based; default: last line)\n" +
        "       --bind ADDR: listen address (default 127.0.0.1 = loopback only, also answers 'localhost');\n" +
        "         0.0.0.0, ::, * or + = every interface (containers); otherwise one IPv4/IPv6 address or a host name\n" +
        "       --knowledge DIR: rules-text localisation (knowledge/, knowledge/source or one version dir);\n" +
        "         default: env STS2SIM_KNOWLEDGE, else the nearest knowledge/source above the cwd or the binary\n" +
        "       --reveal-moves: games default to \"reveal_moves\": true (intents and the enemy-turn log name the move);\n" +
        "         default off, as in the game (a create body's \"reveal_moves\" overrides it per game)\n" +
        "       env STS2SIM_VERSION: version string reported by /health (default: git rev-parse --short HEAD, else 'unknown')";

    // Throws ArgumentException / FormatException on a bad command line.
    public static ServerArgs ParseArgs(string[] args)
    {
        var o = new ServerArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "--port":
                    o.Port = int.Parse(Next(), CultureInfo.InvariantCulture);
                    if (o.Port is < 1 or > 65535)
                    {
                        throw new ArgumentException($"--port {o.Port} is outside 1..65535");
                    }
                    break;
                case "--bind":
                    o.Bind = HttpHost.ValidateBind(Next());
                    break;
                case "--crash-log":
                    o.CrashLog = Next();
                    break;
                case "--no-crash-log":
                    o.CrashLog = null;
                    break;
                case "--selftest":
                    o.SelfTest = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--seed":
                    o.Seed = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--character":
                    o.Characters.Add(Next().ToUpperInvariant());
                    break;
                case "--ascension":
                    o.Ascension = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--content":
                    o.Content = Next();
                    break;
                case "--policy":
                    o.Policy = Next();
                    break;
                case "--step-cap":
                    o.StepCap = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "--replay":
                    o.Replay = Next();
                    break;
                case "--knowledge":
                    o.Knowledge = Next();
                    break;
                case "--reveal-moves":
                    o.RevealMoves = true;
                    break;
                case "--line":
                    o.Line = int.Parse(Next(), CultureInfo.InvariantCulture);
                    break;
                case "-h":
                case "--help":
                    o.Help = true;
                    break;
                default:
                    throw new ArgumentException($"unexpected argument '{a}'");
            }
        }
        return o;
    }

    public static int Main(string[] args)
    {
        ServerArgs o;
        try
        {
            o = ParseArgs(args);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }
        if (o.Help)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        RulesText.Load(o.Knowledge);
        var service = new PlayService(new PlayServiceOptions { Version = GitVersion(), CrashLogPath = o.CrashLog, RevealMoves = o.RevealMoves });
        if (o.Replay != null)
        {
            return Replay.Cli(o.Replay, o.Line);
        }
        if (o.SelfTest is { } n)
        {
            return SelfTest.Cli(service, new SelfTestOptions
            {
                Runs = n,
                FirstSeed = o.Seed,
                Characters = o.Characters.Count > 0 ? o.Characters : null,
                Ascension = o.Ascension,
                Content = o.Content,
                StepCap = o.StepCap,
                Policy = o.Policy
            });
        }
        return HttpHost.Serve(service, o.Port, o.Bind);
    }

    public static string GitVersion()
    {
        string? env = Environment.GetEnvironmentVariable("STS2SIM_VERSION");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                return "unknown";
            }
            string sha = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return p.ExitCode == 0 && sha.Length > 0 ? sha : "unknown";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
