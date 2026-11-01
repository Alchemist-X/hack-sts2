using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Sts2Sim.Core;
using Sts2Sim.Core.Run;
using Sts2Sim.Diff.Run;

namespace Sts2Sim.Tests;

public static class RunOracleTests
{
    private const string FixtureName = "run_save_a8_silent_boss1.save";

    private static string FixturePath([CallerFilePath] string source = "")
        => Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", FixtureName);

    private static RunRecord FixtureRecord() => RunRecordReader.FromSave(RunSaveReader.ReadFile(FixturePath()), FixturePath());

    private static RunRecord EditedRecord(Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        edit(root);
        return RunRecordReader.FromSave(RunSaveReader.Read(root.ToJsonString()), "edited");
    }

    private static string HistoryJson(string buildId)
    {
        var save = JsonNode.Parse(File.ReadAllText(FixturePath()))!.AsObject();
        var run = new JsonObject
        {
            ["schema_version"] = RunRecord.SupportedHistorySchemaVersion,
            ["build_id"] = buildId,
            ["game_mode"] = "standard",
            ["seed"] = save["rng"]!["seed"]!.GetValue<string>(),
            ["ascension"] = save["ascension"]!.GetValue<int>(),
            ["acts"] = new JsonArray(save["acts"]!.AsArray().Select(a => (JsonNode?)JsonValue.Create(a!["id"]!.GetValue<string>())).ToArray()),
            ["modifiers"] = new JsonArray(),
            ["players"] = new JsonArray(new JsonObject
            {
                ["character"] = save["players"]![0]!["character_id"]!.GetValue<string>(),
                ["relics"] = new JsonArray(),
                ["deck"] = new JsonArray()
            }),
            ["map_point_history"] = save["map_point_history"]!.DeepClone()
        };
        return run.ToJsonString();
    }

    private static RunOracleCheck Single(IEnumerable<RunOracleCheck> checks, string oracle) => checks.Single(c => c.Oracle == oracle);

    public static void Test_RunOracle_NeowAgreesOnFixtureSave()
    {
        var check = NeowOracle.Check(FixtureRecord(), new RunOracleOptions());
        Assert.Equal(RunOracleStatus.Agree, check.Status, check.Detail);
        Assert.Equal(3, check.Compared);
    }

    public static void Test_RunOracle_NeowDetectsAWrongOptionOrder()
    {
        var record = EditedRecord(root =>
        {
            var choices = root["map_point_history"]![0]![0]!["player_stats"]![0]!["ancient_choice"]!.AsArray();
            var first = choices[0]!.DeepClone();
            choices[0] = choices[1]!.DeepClone();
            choices[1] = first;
        });
        Assert.Equal(RunOracleStatus.Diverge, NeowOracle.Check(record, new RunOracleOptions()).Status);
    }

    public static void Test_RunOracle_UnknownRollsAgreeOnFixtureSave()
    {
        var check = UnknownRollOracle.Check(FixtureRecord(), new RunOracleOptions());
        Assert.Equal(RunOracleStatus.Agree, check.Status, check.Detail);
        Assert.Equal(4, check.Compared, "four unknown points on the act-1 path");
    }

    public static void Test_RunOracle_SavedMapAndPathAgreeOnFixtureSave()
    {
        var checks = MapOracle.Check(FixtureRecord(), new RunOracleOptions());
        var saved = Single(checks, RunOracleNames.MapSaved);
        Assert.Equal(RunOracleStatus.Agree, saved.Status, saved.Detail);
        var path = Single(checks, RunOracleNames.MapPath);
        Assert.Equal(RunOracleStatus.Agree, path.Status, path.Detail);
        Assert.Equal(17, path.Compared);
        var generated = Single(checks, RunOracleNames.MapGenerated);
        Assert.True(generated.Status is RunOracleStatus.Agree or RunOracleStatus.Unsupported, generated.Detail);
    }

    public static void Test_RunOracle_AllOraclesReportWithoutErrors()
    {
        var checks = RunOracleRunner.CheckRecord(FixtureRecord(), new RunOracleOptions());
        Assert.SequenceEqual(RunOracleNames.All.OrderBy(n => n, StringComparer.Ordinal), checks.Select(c => c.Oracle).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var c in checks)
        {
            Assert.True(c.Status != RunOracleStatus.Error, c.Oracle + ": " + c.Detail);
            Assert.True(c.Status != RunOracleStatus.Diverge, c.Oracle + ": " + c.Detail);
        }
    }

    public static void Test_RunOracle_HistoryRecordParsesAndGatesTheBuild()
    {
        var record = RunRecordReader.ReadHistory(HistoryJson(RunRecord.SupportedBuildId), "fixture.run");
        Assert.Equal(RunRecordKind.History, record.Kind);
        Assert.Equal("SILENT", record.CharacterId);
        Assert.Equal(17, record.Floors[0].Count);
        Assert.Equal(MapPointType.Ancient, record.Floors[0][0].PointType);
        Assert.Equal("KALEIDOSCOPE", record.Floors[0][0].AncientChoices.Single(c => c.WasChosen).TextKey);
        var checks = RunOracleRunner.CheckRecord(record, new RunOracleOptions());
        Assert.Equal(RunOracleStatus.Agree, Single(checks, RunOracleNames.Neow).Status);
        Assert.Equal(RunOracleStatus.Agree, Single(checks, RunOracleNames.UnknownRolls).Status, "the act-1 path rolls agree without the map too");

        var old = RunRecordReader.ReadHistory(HistoryJson("v0.110.1"), "old.run");
        var gated = RunOracleRunner.CheckRecord(old, new RunOracleOptions());
        Assert.True(gated.All(c => c.Status == RunOracleStatus.Unsupported), "other builds are not compared");
        Assert.Equal(RunOracleStatus.Agree, Single(RunOracleRunner.CheckRecord(old, new RunOracleOptions { AnyBuild = true }), RunOracleNames.Neow).Status);
    }

    public static void Test_RunOracle_RunModeCliOnFixture()
    {
        Assert.Equal(0, Sts2Sim.Diff.Program.Main(new[] { "run", FixturePath() }));
        Assert.Equal(2, Sts2Sim.Diff.Program.Main(new[] { "run" }));
    }
}
