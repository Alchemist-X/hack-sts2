using System.Globalization;

namespace Sts2Sim.Diff.Replay;

public static class ReplayCli
{
    private const string Usage = "usage: Sts2Sim.Diff replay-info <file.mcr|dir> [-v] | replay-convert <file.mcr|dir> <out-dir>";

    public static int Run(string[] args)
    {
        string cmd = args[0];
        var rest = args[1..];
        return cmd switch
        {
            "replay-info" => Info(rest),
            "replay-convert" => McrBundleConverter.ConvertMain(rest),
            "replay-dump" => Dump(rest),
            "replay-events" => Events(rest),
            _ => Fail()
        };
    }

    private static int Fail()
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }

    public static List<string> DiscoverReplays(string input)
    {
        string full = Path.GetFullPath(input);
        if (File.Exists(full))
        {
            return new List<string> { full };
        }
        if (!Directory.Exists(full))
        {
            throw new IOException($"{input} is neither a file nor a directory");
        }
        return Directory.EnumerateFiles(full, "*.mcr", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    public static string? CombatEncounterHint(McrReplay replay)
    {
        var first = replay.Checksums.Count > 0 ? replay.Checksums[0].State : null;
        if (first == null)
        {
            return null;
        }
        return string.Join("+", first.Creatures.Where(c => c.MonsterId != null).Select(c => c.MonsterId));
    }

    private static int Events(string[] args)
    {
        if (args.Length != 1)
        {
            return Fail();
        }
        var replay = McrReader.ReadFile(args[0]);
        Console.WriteLine($"nextActionId={replay.NextActionId} nextHookId={replay.NextHookId} nextChecksumId={replay.NextChecksumId} choiceIds=[{string.Join(",", replay.ChoiceIds)}]");
        int i = 0;
        foreach (var e in replay.Events)
        {
            string detail = e.Type switch
            {
                McrEventType.GameAction => e.Action!.ToString(),
                McrEventType.HookAction => $"hook {e.HookId} {e.GameActionType}",
                McrEventType.ResumeAction => $"resume {e.ActionId}",
                McrEventType.PlayerChoice => $"choice {e.ChoiceId} {e.Choice!.Type} combat=[{string.Join(",", e.Choice.CombatCards ?? new())}] idx=[{string.Join(",", e.Choice.Indexes ?? new())}] deck=[{string.Join(",", e.Choice.DeckCards ?? new())}]",
                _ => e.Type.ToString()
            };
            Console.WriteLine($"E{i++} {detail}");
        }
        foreach (var c in replay.Checksums)
        {
            var p = c.State.Players.FirstOrDefault();
            string hand = p == null ? "" : string.Join(",", p.Pile(McrPileType.Hand)?.Cards.Select(x => x.Card.Id + (x.Card.CurrentUpgradeLevel > 0 ? "+" : "")) ?? Array.Empty<string>());
            string mons = string.Join(" ", c.State.Creatures.Select(x => (x.MonsterId ?? "P") + ":" + x.CurrentHp + "/" + x.Block));
            Console.WriteLine($"C{c.Id} '{c.Context}' act={c.State.LastExecutedActionId} hook={c.State.LastExecutedHookId} turn={p?.TurnNumber} {p?.Phase} e={p?.Energy} hand=[{hand}] {mons}");
        }
        return 0;
    }

    private static int Dump(string[] args)
    {
        if (args.Length != 1)
        {
            return Fail();
        }
        var arr = new JsonArray();
        foreach (string file in DiscoverReplays(args[0]))
        {
            var replay = McrReader.ReadFile(file);
            var p = replay.Player;
            arr.Add(new JsonObject
            {
                ["file"] = file,
                ["version"] = replay.Version,
                ["hash_verified"] = replay.HashVerified,
                ["seed"] = replay.Run.Rng.Seed,
                ["character"] = p?.CharacterId,
                ["floor"] = replay.TotalFloor + 1,
                ["encounter"] = McrBundleConverter.ResolveEncounter(replay).EncounterId,
                ["deck"] = new JsonArray((p?.Deck ?? new()).Select(c => (JsonNode?)JsonValue.Create(c.Id + (c.CurrentUpgradeLevel > 0 ? "+" : string.Empty))).ToArray()),
                ["relics"] = new JsonArray((p?.Relics ?? new()).Select(c => (JsonNode?)JsonValue.Create(c.Id)).ToArray()),
                ["potions"] = new JsonArray((p?.Potions ?? new()).Select(c => (JsonNode?)JsonValue.Create(c.Id)).ToArray())
            });
        }
        Console.WriteLine(arr.ToJsonString());
        return 0;
    }

    private static int Info(string[] args)
    {
        bool verbose = args.Contains("-v") || args.Contains("--verbose");
        var inputs = args.Where(a => !a.StartsWith('-')).ToList();
        if (inputs.Count != 1)
        {
            return Fail();
        }
        List<string> files;
        try
        {
            files = DiscoverReplays(inputs[0]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"cannot read {inputs[0]}: {ex.Message}");
            return 2;
        }
        int ok = 0;
        int failed = 0;
        long events = 0;
        long checksums = 0;
        var actionCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            McrReplay replay;
            try
            {
                replay = McrReader.ReadFile(file);
            }
            catch (Exception ex) when (ex is McrFormatException or IOException or UnauthorizedAccessException)
            {
                failed++;
                Console.WriteLine($"FAIL {file}: {ex.Message}");
                continue;
            }
            ok++;
            events += replay.Events.Count;
            checksums += replay.Checksums.Count;
            var perFile = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in replay.Events)
            {
                string key = e.Type == McrEventType.GameAction ? e.Action!.Name : e.Type.ToString();
                perFile[key] = perFile.GetValueOrDefault(key) + 1;
                actionCounts[key] = actionCounts.GetValueOrDefault(key) + 1;
            }
            var p = replay.Player;
            string line = string.Create(CultureInfo.InvariantCulture,
                $"{file}\n  {replay.Version} {replay.GitCommit} table={replay.Table.GameVersion}{(replay.HashVerified ? "" : "(hash-unverified)")} seed={replay.Run.Rng.Seed} {p?.CharacterId} A{replay.Run.Ascension} act={replay.Run.CurrentActIndex + 1} floor={replay.TotalFloor + 1} hp={p?.CurrentHp}/{p?.MaxHp} deck={p?.Deck.Count} relics={p?.Relics.Count} potions={p?.Potions.Count} monsters={CombatEncounterHint(replay)}\n  events={replay.Events.Count} [{string.Join(", ", perFile.Select(kv => kv.Key + "=" + kv.Value))}] checksums={replay.Checksums.Count}");
            Console.WriteLine(line);
            if (verbose)
            {
                var enc = McrBundleConverter.ResolveEncounter(replay);
                Console.WriteLine($"  encounter={enc.EncounterId ?? "?"} ({enc.Reason})");
            }
        }
        Console.WriteLine($"replays: {files.Count} decoded: {ok} failed: {failed} events: {events} checksums: {checksums}");
        Console.WriteLine("event types: " + string.Join(", ", actionCounts.Select(kv => kv.Key + "=" + kv.Value)));
        return failed > 0 ? 1 : 0;
    }
}
