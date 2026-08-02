using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using Sts2Recorder.Core;

namespace Sts2Recorder.Game;

/// <summary>
/// Mod entry point. Loads config, applies every Harmony patch individually
/// (a failed patch degrades that one hook instead of killing the mod), wires
/// the static event subscriptions allowed at init time, and owns the
/// ProcessFrame pump used for deferred main-thread work.
/// Init-timing contract (docs/hook-map.md): no run/model/loc access here.
/// </summary>
[ModInitializer("Initialize")]
public static class RecorderMod
{
    public const string Version = "0.2.0";
    public const string HarmonyId = "alchemist-x.sts2recorder";
    private const string ConfigFileName = "Sts2Recorder.conf";

    /// <summary>Game versions this recorder was verified against (manifest untested flag).</summary>
    public static readonly IReadOnlyList<string> KnownGoodVersions = new[] { "v0.107.1" };

    private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private static readonly List<string> _startupDegradedHooks = new();
    private static readonly object _degradedLock = new();

    /// <summary>Active recording session; null between runs and during MP/replay/test runs.</summary>
    public static TrajectorySession? Session { get; internal set; }

    /// <summary>RunState cached from RunManager.RunStarted (RunManager.State is private).</summary>
    public static RunState? CurrentRunState { get; internal set; }

    /// <summary>Root directory for session output; never under the game's mods/ tree.</summary>
    public static string OutputRoot { get; private set; } = DefaultOutputRoot();

    /// <summary>
    /// True when a lightweight training worker should expose MCP state/actions
    /// without installing recorder hooks or writing duplicate trajectories.
    /// Controlled per process by STS2_RECORDER_DISABLED.
    /// </summary>
    public static bool RecordingDisabled { get; private set; }

    /// <summary>
    /// Trailing-coalesce window for StateTracker snapshot bursts, in ms
    /// (Sts2Recorder.conf key "snapshot_min_interval_ms"; 0 disables the throttle).
    /// </summary>
    public static double SnapshotMinIntervalMs { get; private set; } = DefaultSnapshotMinIntervalMs;

    public const double DefaultSnapshotMinIntervalMs = 150;

    public static void Initialize()
    {
        try
        {
            LoadConfig();
            if (RecordingDisabled)
            {
                GD.Print("[Sts2Recorder] disabled for this text worker "
                    + "(STS2_RECORDER_DISABLED=1)");
                return;
            }
            var harmony = new Harmony(HarmonyId);
            RunLifecycle.ApplyPatches(harmony);
            ActionPipeline.ApplyPatches(harmony);
            HumanFunnels.ApplyPatches(harmony);

            TrySubscribe("event:RunManager.RunStarted",
                () => RunManager.Instance.RunStarted += RunLifecycle.OnRunStarted);
            EventTap.SubscribeStaticEvents();
            Snapshots.SubscribeStaticEvents();

            var tree = (SceneTree)Engine.GetMainLoop();
            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(DrainMainThreadQueue));

            GD.Print($"[Sts2Recorder] v{Version} initialized; output root: {OutputRoot}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Sts2Recorder] Failed to initialize: {ex}");
        }
    }

    /// <summary>
    /// Applies one Harmony patch inside its own try/catch. The target lookup is a
    /// lazy resolver so that lookup failures (missing method after a game update,
    /// AmbiguousMatchException on new overloads) are ALSO caught here — argument-side
    /// evaluation at the call site would escape the isolation. A null/failed lookup
    /// or a patch failure registers the hook id as degraded and logs; it never throws.
    /// </summary>
    internal static void TryPatch(
        Harmony harmony,
        string hookId,
        Func<MethodBase?> resolveOriginal,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null,
        HarmonyMethod? finalizer = null)
    {
        try
        {
            MethodBase? original = resolveOriginal();
            if (original == null)
            {
                throw new MissingMethodException($"target method not found for {hookId}");
            }
            harmony.Patch(original, prefix: prefix, postfix: postfix, finalizer: finalizer);
        }
        catch (Exception ex)
        {
            RegisterDegradedHook(hookId, ex);
        }
    }

    /// <summary>Runs one event subscription inside its own try/catch; failure degrades the hook.</summary>
    internal static void TrySubscribe(string hookId, Action subscribe)
    {
        try
        {
            subscribe();
        }
        catch (Exception ex)
        {
            RegisterDegradedHook(hookId, ex);
        }
    }

    /// <summary>Startup-time degraded hooks, copied into every new session's manifest.</summary>
    internal static IReadOnlyList<string> StartupDegradedHooks
    {
        get
        {
            lock (_degradedLock)
            {
                return _startupDegradedHooks.ToArray();
            }
        }
    }

    /// <summary>Reports a handler failure without ever throwing back into game code.</summary>
    internal static void Report(string where, Exception ex)
    {
        try
        {
            var session = Session;
            if (session != null)
            {
                session.RecordError(where, ex);
            }
            else
            {
                GD.PrintErr($"[Sts2Recorder] {where}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        catch
        {
            // Last-resort swallow: the recorder must never crash the game.
        }
    }

    /// <summary>Queues work for the next ProcessFrame on the main thread (no polling).</summary>
    internal static void RunOnMainThread(Action work)
    {
        _mainThreadQueue.Enqueue(work);
    }

    /// <summary>Converts Godot user:// paths to absolute OS paths; passes others through.</summary>
    internal static string GlobalizePath(string path)
    {
        try
        {
            if (path.StartsWith("user://", StringComparison.Ordinal)
                || path.StartsWith("res://", StringComparison.Ordinal))
            {
                return ProjectSettings.GlobalizePath(path);
            }
        }
        catch (Exception ex)
        {
            Report("RecorderMod.GlobalizePath", ex);
        }
        return path;
    }

    private static void RegisterDegradedHook(string hookId, Exception ex)
    {
        lock (_degradedLock)
        {
            if (!_startupDegradedHooks.Contains(hookId))
            {
                _startupDegradedHooks.Add(hookId);
            }
        }
        GD.PrintErr($"[Sts2Recorder] Hook degraded ({hookId}): {ex.GetType().Name}: {ex.Message}");
        try
        {
            Session?.AddDegradedHook(hookId);
        }
        catch
        {
            // Session may be mid-teardown; the startup list still covers the next run.
        }
    }

    private static void DrainMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var work))
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                Report("RecorderMod.DrainMainThreadQueue", ex);
            }
        }
    }

    private static string DefaultOutputRoot()
    {
        return Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
            "Sts2Recorder");
    }

    private static void LoadConfig()
    {
        try
        {
            var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (modDir == null)
            {
                return;
            }
            var configPath = Path.Combine(modDir, ConfigFileName);
            if (File.Exists(configPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                if (doc.RootElement.TryGetProperty("output_root", out var rootElem)
                    && rootElem.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(rootElem.GetString()))
                {
                    OutputRoot = rootElem.GetString()!;
                }
                if (doc.RootElement.TryGetProperty("snapshot_min_interval_ms", out var intervalElem)
                    && intervalElem.ValueKind == JsonValueKind.Number)
                {
                    SetSnapshotInterval(intervalElem.GetDouble(), ConfigFileName);
                }
            }

            // Process-level overrides make one immutable mod/runtime directory
            // safe to share across concurrent text workers.
            var outputRoot = System.Environment.GetEnvironmentVariable(
                "STS2_RECORDER_OUTPUT_ROOT");
            if (!string.IsNullOrWhiteSpace(outputRoot))
            {
                OutputRoot = outputRoot;
            }
            var intervalText = System.Environment.GetEnvironmentVariable(
                "STS2_RECORDER_SNAPSHOT_MIN_INTERVAL_MS");
            if (!string.IsNullOrWhiteSpace(intervalText))
            {
                if (double.TryParse(
                    intervalText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var interval))
                {
                    SetSnapshotInterval(interval, "STS2_RECORDER_SNAPSHOT_MIN_INTERVAL_MS");
                }
                else
                {
                    GD.PrintErr($"[Sts2Recorder] Ignoring invalid "
                        + $"STS2_RECORDER_SNAPSHOT_MIN_INTERVAL_MS={intervalText}");
                }
            }
            var disabled = System.Environment.GetEnvironmentVariable(
                "STS2_RECORDER_DISABLED");
            RecordingDisabled = string.Equals(disabled, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(disabled, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(disabled, "yes", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Sts2Recorder] Failed to read {ConfigFileName}: {ex.Message}; using defaults");
        }
    }

    private static void SetSnapshotInterval(double interval, string source)
    {
        if (interval >= 0 && double.IsFinite(interval))
        {
            SnapshotMinIntervalMs = interval;
            return;
        }
        GD.PrintErr(
            $"[Sts2Recorder] Ignoring invalid snapshot interval {interval} from {source}; "
            + $"using {SnapshotMinIntervalMs} ms");
    }
}
