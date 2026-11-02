using System.Diagnostics;
using System.Globalization;

namespace Sts2Sim.Server;

public static class Program
{
    private const string Usage =
        "usage: Sts2Sim.Server [--port 8765] [--crash-log tmp/play-crashes.jsonl | --no-crash-log] [--knowledge DIR]\n" +
        "       Sts2Sim.Server --selftest N [--seed 1] [--character IRONCLAD] [--ascension 0] [--content ported_only|all] [--step-cap 6000] [--policy random|biased]\n" +
        "       Sts2Sim.Server --replay tmp/play-crashes.jsonl [--line N]   (N is 1-based; default: last line)\n" +
        "       --knowledge DIR: rules-text localisation (knowledge/, knowledge/source or one version dir);\n" +
        "         default: env STS2SIM_KNOWLEDGE, else the nearest knowledge/source above the cwd or the binary";

    public static int Main(string[] args)
    {
        int port = 8765;
        string? crashLog = "tmp/play-crashes.jsonl";
        int? selftest = null;
        string? replay = null;
        int? line = null;
        int seed = 1;
        int ascension = 0;
        int stepCap = 6_000;
        string content = "ported_only";
        string policy = "random";
        string? knowledge = null;
        var characters = new List<string>();
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
                switch (a)
                {
                    case "--port":
                        port = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--crash-log":
                        crashLog = Next();
                        break;
                    case "--no-crash-log":
                        crashLog = null;
                        break;
                    case "--selftest":
                        selftest = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--seed":
                        seed = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--character":
                        characters.Add(Next().ToUpperInvariant());
                        break;
                    case "--ascension":
                        ascension = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--content":
                        content = Next();
                        break;
                    case "--policy":
                        policy = Next();
                        break;
                    case "--step-cap":
                        stepCap = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--replay":
                        replay = Next();
                        break;
                    case "--knowledge":
                        knowledge = Next();
                        break;
                    case "--line":
                        line = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "-h":
                    case "--help":
                        Console.WriteLine(Usage);
                        return 0;
                    default:
                        throw new ArgumentException($"unexpected argument '{a}'");
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        RulesText.Load(knowledge);
        var service = new PlayService(new PlayServiceOptions { Version = GitVersion(), CrashLogPath = crashLog });
        if (replay != null)
        {
            return Replay.Cli(replay, line);
        }
        if (selftest is { } n)
        {
            return SelfTest.Cli(service, new SelfTestOptions
            {
                Runs = n,
                FirstSeed = seed,
                Characters = characters.Count > 0 ? characters : null,
                Ascension = ascension,
                Content = content,
                StepCap = stepCap,
                Policy = policy
            });
        }
        return HttpHost.Serve(service, port);
    }

    public static string GitVersion()
    {
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
