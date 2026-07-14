using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2Recorder.Core;

namespace Sts2Recorder.Game;

/// <summary>
/// Session lifecycle: RunStarted attach (with the multiplayer/replay/test guard),
/// SessionMeta construction, terminal handling (OnEnded first-call latch +
/// CreateRunHistoryEntry catch-all), CleanUp teardown, save heartbeats, and
/// native artifact archiving (.run / latest.mcr copies).
/// </summary>
public static class RunLifecycle
{
    private static bool _terminalLatched;
    private static bool _savedSubscribed;
    private static AccessTools.FieldRef<RunManager, long>? _startTimeRef;

    internal static void ApplyPatches(Harmony harmony)
    {
        _startTimeRef = JsonDescribe.Try(
            () => AccessTools.FieldRefAccess<RunManager, long>("_startTime"));

        RecorderMod.TryPatch(
            harmony,
            "patch:RunManager.OnEnded",
            AccessTools.Method(typeof(RunManager), nameof(RunManager.OnEnded)),
            postfix: new HarmonyMethod(typeof(RunLifecycle), nameof(OnEndedPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:RunHistoryUtilities.CreateRunHistoryEntry",
            AccessTools.Method(typeof(RunHistoryUtilities), nameof(RunHistoryUtilities.CreateRunHistoryEntry)),
            postfix: new HarmonyMethod(typeof(RunLifecycle), nameof(CreateRunHistoryEntryPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:RunManager.CleanUp",
            AccessTools.Method(typeof(RunManager), nameof(RunManager.CleanUp)),
            prefix: new HarmonyMethod(typeof(RunLifecycle), nameof(CleanUpPrefix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:RunHistorySaveManager.SaveHistory",
            AccessTools.Method(typeof(RunHistorySaveManager), nameof(RunHistorySaveManager.SaveHistory)),
            postfix: new HarmonyMethod(typeof(RunLifecycle), nameof(SaveHistoryPostfix)));
        RecorderMod.TryPatch(
            harmony,
            "patch:CombatReplayWriter.WriteReplay",
            AccessTools.Method(typeof(CombatReplayWriter), nameof(CombatReplayWriter.WriteReplay)),
            postfix: new HarmonyMethod(typeof(RunLifecycle), nameof(WriteReplayPostfix)));
    }

    /// <summary>
    /// RunManager.RunStarted handler — the primary attach point. Fires for new,
    /// resumed, MP, and replay runs; the guard below keeps v1 singleplayer-only.
    /// </summary>
    public static void OnRunStarted(RunState state)
    {
        try
        {
            // Multiplayer/replay/test guard, exactly per docs/hook-map.md:
            // IsMultiplayer() is Host|Client only; Replay must be checked separately.
            var netType = RunManager.Instance.NetService.Type;
            if (netType.IsMultiplayer() || netType == NetGameType.Replay || TestMode.IsOn)
            {
                GD.Print($"[Sts2Recorder] Not recording this run (net type {netType}, test mode {TestMode.IsOn}).");
                return;
            }
            if (RecorderMod.Session != null)
            {
                // Missed CleanUp (should not happen); close the stale session first.
                CloseSession();
            }

            var meta = BuildMeta(state);
            var session = TrajectorySession.Begin(RecorderMod.OutputRoot, meta, SystemClock.Instance);
            foreach (var hookId in RecorderMod.StartupDegradedHooks)
            {
                session.AddDegradedHook(hookId);
            }
            _terminalLatched = false;
            RecorderMod.CurrentRunState = state;
            RecorderMod.Session = session;

            if (meta.Untested)
            {
                GD.PrintErr(
                    $"[Sts2Recorder] Game version {meta.GameVersion} is not in the known-good list "
                    + $"[{string.Join(", ", RecorderMod.KnownGoodVersions)}]; recording anyway with untested=true.");
            }

            // Per-run objects (ActionQueueSet, synchronizers, ChecksumTracker, ...)
            // are recreated in InitializeShared each run — resubscribe everything.
            ActionPipeline.OnRunStarted();
            EventTap.OnRunStarted(session);
            HumanFunnels.OnRunStarted(session);
            SubscribeSavedHeartbeat(session);

            Snapshots.TakeSnapshot("phase");
            GD.Print($"[Sts2Recorder] Recording session started: {session.Directory}");
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.OnRunStarted", ex);
        }
    }

    private static SessionMeta BuildMeta(RunState state)
    {
        var release = JsonDescribe.Try<ReleaseInfo>(() => ReleaseInfoManager.Instance.ReleaseInfo);
        var gameVersion = release?.Version ?? "UNRELEASED";
        var gameCommit = release?.Commit ?? "unknown";
        var seed = JsonDescribe.Try(() => state.Rng.StringSeed) ?? "unknown";
        var character = JsonDescribe.Try(() => LocalContext.GetMe(state)?.Character.Id.Entry)
            ?? JsonDescribe.Try(() => state.Players.Count > 0 ? state.Players[0].Character.Id.Entry : null)
            ?? "unknown";
        var profile = JsonDescribe.Try(() => SaveManager.Instance.CurrentProfileId.ToString()) ?? "unknown";
        var startTime = ReadStartTime();

        return new SessionMeta(
            RecorderVersion: RecorderMod.Version,
            GameVersion: gameVersion,
            GameCommit: gameCommit,
            // Steam appmanifest build_id is not runtime-readable; version+commit identify the build.
            BuildId: "",
            Platform: JsonDescribe.Try(() => OS.GetName()) ?? "unknown",
            Profile: profile,
            Seed: seed,
            Character: character,
            Ascension: state.AscensionLevel,
            GameMode: ComputeGameMode(state),
            StartTime: startTime,
            Untested: !IsKnownGoodVersion(gameVersion),
            Mods: ListLoadedMods());
    }

    /// <summary>
    /// v0.107.1: RunState carries a first-class GameMode enum (None/Standard/Daily/Custom,
    /// Core/Runs/GameMode.cs); saves persist it as game_mode since SerializableRun v15.
    /// GameMode.None falls back to the v0.99.1 mirror of RunManager's private GameMode.
    /// </summary>
    private static string ComputeGameMode(RunState state)
    {
        try
        {
            switch (state.GameMode)
            {
                case GameMode.Daily:
                    return "daily";
                case GameMode.Custom:
                    return "custom";
                case GameMode.Standard:
                    return "standard";
            }
            if (state.Modifiers.Count > 0)
            {
                return RunManager.Instance.DailyTime.HasValue ? "daily" : "custom";
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.ComputeGameMode", ex);
        }
        return "standard";
    }

    private static long ReadStartTime()
    {
        try
        {
            if (_startTimeRef != null)
            {
                var value = _startTimeRef(RunManager.Instance);
                if (value > 0)
                {
                    return value;
                }
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.ReadStartTime", ex);
        }
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static bool IsKnownGoodVersion(string version)
    {
        var normalized = version.TrimStart('v', 'V');
        foreach (var known in RecorderMod.KnownGoodVersions)
        {
            if (string.Equals(known.TrimStart('v', 'V'), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static IReadOnlyList<string> ListLoadedMods()
    {
        var mods = new List<string>();
        try
        {
            // v0.107.1: ModManager.LoadedMods/AllMods removed; GetLoadedMods() filters
            // ModManager.Mods by the new Mod.state == ModLoadState.Loaded.
            foreach (var mod in ModManager.GetLoadedMods())
            {
                var id = mod.manifest?.id ?? "unknown";
                var version = mod.manifest?.version;
                mods.Add(string.IsNullOrEmpty(version) ? id : $"{id}@{version}");
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.ListLoadedMods", ex);
        }
        return mods;
    }

    private static void SubscribeSavedHeartbeat(TrajectorySession session)
    {
        if (_savedSubscribed)
        {
            return;
        }
        try
        {
            // The public forwarding event onto RunSaveManager.Saved; fires after the
            // file write completes. This is the crash-resilience flush (CleanUp is
            // never called on process kill).
            SaveManager.Instance.Saved += OnSaved;
            _savedSubscribed = true;
        }
        catch (Exception ex)
        {
            session.AddDegradedHook("event:SaveManager.Saved");
            RecorderMod.Report("RunLifecycle.SubscribeSavedHeartbeat", ex);
        }
    }

    private static void OnSaved()
    {
        try
        {
            RecorderMod.Session?.Flush();
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.OnSaved", ex);
        }
    }

    /// <summary>
    /// Terminal handler with first-call latch. OnEnded fires TWICE on victory
    /// (WinRun -> OnEnded(true), then GuaranteeKillAllPlayers -> OnEnded(false));
    /// the first invocation is authoritative. CreateRunHistoryEntry postfix uses
    /// the same latch as the catch-all for main-menu abandons.
    /// </summary>
    private static void Terminal(bool win, bool abandoned)
    {
        var session = RecorderMod.Session;
        if (session == null || _terminalLatched)
        {
            return;
        }
        _terminalLatched = true;
        Snapshots.TakeSnapshot("phase");
        session.RecordEvent("run_ended", new JsonObject
        {
            ["win"] = win,
            ["abandoned"] = abandoned,
        });
        session.Complete(new RunResult(win, abandoned, SystemClock.Instance.Now));
        GD.Print($"[Sts2Recorder] Run ended (win={win}, abandoned={abandoned}); session finalized.");
    }

    private static void CloseSession()
    {
        var session = RecorderMod.Session;
        RecorderMod.Session = null;
        RecorderMod.CurrentRunState = null;
        _terminalLatched = false;
        if (session == null)
        {
            return;
        }
        try
        {
            EventTap.OnRunEnding();
            HumanFunnels.OnRunEnding();
            ActionPipeline.OnRunEnding();
            session.Flush();
        }
        finally
        {
            session.Dispose();
        }
    }

    private static void OnEndedPostfix(bool isVictory)
    {
        try
        {
            // Disambiguate abandon-vs-death via IsAbandoned inside the postfix.
            Terminal(isVictory, JsonDescribe.Try<bool?>(() => RunManager.Instance.IsAbandoned) ?? false);
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.OnEndedPostfix", ex);
        }
    }

    private static void CreateRunHistoryEntryPostfix(SerializableRun run, bool victory, bool isAbandoned)
    {
        try
        {
            Terminal(victory, isAbandoned);
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.CreateRunHistoryEntryPostfix", ex);
        }
    }

    /// <summary>
    /// PREFIX (not postfix): CleanUp's finally block nulls RunManager.State and
    /// LocalContext.NetId — this is the last window for a graceful flush.
    /// </summary>
    private static void CleanUpPrefix()
    {
        try
        {
            CloseSession();
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.CleanUpPrefix", ex);
        }
    }

    /// <summary>Archives the game's just-written .run history file (copy, never move).</summary>
    private static void SaveHistoryPostfix(RunHistory history)
    {
        try
        {
            var session = RecorderMod.Session;
            if (session == null)
            {
                return;
            }
            // Path resolved at runtime via the profile-scoped provider (the modded/
            // relocation changes the tree); mirror RunHistorySaveManager's layout.
            var path = SaveManager.Instance.GetProfileScopedPath($"saves/history/{history.StartTime}.run");
            path = RecorderMod.GlobalizePath(path);
            if (System.IO.File.Exists(path))
            {
                session.ArchiveNative(path, "run_history.run");
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.SaveHistoryPostfix", ex);
        }
    }

    /// <summary>
    /// Archives latest.mcr right after the game flushes it. WriteReplay fires on
    /// the combat win path only (losses never flush it) — the .mcr is a bonus
    /// artifact, not the primary event stream.
    /// </summary>
    private static void WriteReplayPostfix(string filePath)
    {
        try
        {
            var session = RecorderMod.Session;
            if (session == null)
            {
                return;
            }
            var path = RecorderMod.GlobalizePath(filePath);
            if (System.IO.File.Exists(path))
            {
                session.ArchiveNative(path, "replay.mcr");
            }
        }
        catch (Exception ex)
        {
            RecorderMod.Report("RunLifecycle.WriteReplayPostfix", ex);
        }
    }
}
