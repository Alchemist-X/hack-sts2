using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    public static void Initialize()
    {
        try
        {
            OutputRoot = LoadOutputRoot();
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

    private static string LoadOutputRoot()
    {
        try
        {
            var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (modDir == null)
            {
                return DefaultOutputRoot();
            }
            var configPath = Path.Combine(modDir, ConfigFileName);
            if (!File.Exists(configPath))
            {
                return DefaultOutputRoot();
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("output_root", out var rootElem)
                && rootElem.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(rootElem.GetString()))
            {
                return rootElem.GetString()!;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Sts2Recorder] Failed to read {ConfigFileName}: {ex.Message}; using default output root");
        }
        return DefaultOutputRoot();
    }
}
