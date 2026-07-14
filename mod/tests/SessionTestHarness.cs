using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sts2Recorder.Core;

namespace Sts2Recorder.Core.Tests;

/// <summary>Shared helpers: temp roots, default meta, JSONL/manifest readers.</summary>
public static class SessionTestHarness
{
    public const long StartTime = 1773034386;
    public const string Seed = "TESTSEED12";

    public static SessionMeta DefaultMeta(string seed = Seed, long startTime = StartTime) =>
        new(
            RecorderVersion: "0.1.0-test",
            GameVersion: "0.108.0",
            GameCommit: "abc123",
            BuildId: "20260701",
            Platform: "macos",
            Profile: "profile1",
            Seed: seed,
            Character: "CHARACTER.DEFECT",
            Ascension: 1,
            GameMode: "standard",
            StartTime: startTime,
            Untested: false,
            Mods: new[] { "Sts2Recorder" });

    public static string NewTempRoot() =>
        Directory.CreateDirectory(
            Path.Combine(
                Path.GetTempPath(),
                "sts2rec-core-tests",
                Guid.NewGuid().ToString("N"))).FullName;

    public static IReadOnlyList<JsonElement> ReadJsonl(string sessionDir, string stream)
    {
        var path = Path.Combine(sessionDir, $"{stream}.jsonl");
        if (!File.Exists(path))
        {
            return Array.Empty<JsonElement>();
        }
        return File.ReadAllLines(path)
            .Where(line => line.Length > 0)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    public static JsonElement ReadManifest(string sessionDir)
    {
        var text = File.ReadAllText(Path.Combine(sessionDir, "manifest.json"));
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static IReadOnlyList<long> AllSeqsInOrder(string sessionDir)
    {
        return new[] { "states", "actions", "events" }
            .SelectMany(stream => ReadJsonl(sessionDir, stream))
            .Select(record => record.GetProperty("seq").GetInt64())
            .OrderBy(seq => seq)
            .ToList();
    }
}
