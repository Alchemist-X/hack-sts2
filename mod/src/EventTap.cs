using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2Recorder.Core;

namespace Sts2Recorder.Game;

/// <summary>
/// CombatHistory tap for events.jsonl (the privileged channel). History.Changed
/// has no payload and also fires on Clear() — new entries are diffed by
/// last-seen count (a decrease means Clear -> resync) and serialized IMMEDIATELY
/// because entries hold live mutable model references. Everything is flushed
/// before CombatEnded tears the history down. Also taps
/// ChecksumTracker.ChecksumGenerated (fires in singleplayer too) for integrity.
/// </summary>
public static class EventTap
{
    private static bool _staticSubscribed;
    private static int _lastSeenCount;
    private static TrajectorySession? _checksumSession;
    private static Action<NetChecksumData, string, NetFullCombatState>? _checksumHandler;

    /// <summary>
    /// CombatManager.Instance is an eager static singleton whose History object
    /// survives across runs — one static subscription, handlers no-op without a
    /// session. Safe at init time per the hook map.
    /// </summary>
    internal static void SubscribeStaticEvents()
    {
        if (_staticSubscribed)
        {
            return;
        }
        _staticSubscribed = true;
        RecorderMod.TrySubscribe("event:CombatManager.CombatSetUp(EventTap)",
            () => CombatManager.Instance.CombatSetUp += OnCombatSetUp);
        RecorderMod.TrySubscribe("event:CombatHistory.Changed",
            () => CombatManager.Instance.History.Changed += OnHistoryChanged);
        RecorderMod.TrySubscribe("event:CombatManager.CombatEnded(EventTap)",
            () => CombatManager.Instance.CombatEnded += OnCombatEnded);
    }

    /// <summary>ChecksumTracker is recreated each run — resubscribe on every RunStarted.</summary>
    internal static void OnRunStarted(TrajectorySession session)
    {
        _lastSeenCount = 0;
        try
        {
            var tracker = RunManager.Instance.ChecksumTracker;
            _checksumSession = session;
            _checksumHandler = OnChecksumGenerated;
            tracker.ChecksumGenerated += _checksumHandler;
        }
        catch (Exception ex)
        {
            session.AddDegradedHook("event:ChecksumTracker.ChecksumGenerated");
            RecorderMod.Report("EventTap.OnRunStarted", ex);
        }
    }

    internal static void OnRunEnding()
    {
        try
        {
            if (_checksumHandler != null)
            {
                RunManager.Instance.ChecksumTracker.ChecksumGenerated -= _checksumHandler;
            }
        }
        catch
        {
            // Tracker may already be disposed during CleanUp.
        }
        _checksumHandler = null;
        _checksumSession = null;
    }

    private static void OnCombatSetUp(CombatState state)
    {
        _lastSeenCount = 0;
    }

    private static void OnCombatEnded(CombatRoom room)
    {
        try
        {
            // Persist before History.Clear(): entries are already serialized in the
            // Changed handler, so an explicit stream flush is all that is needed —
            // this is also the loss-side flush (WriteReplay never fires on losses).
            RecorderMod.Session?.Flush();
        }
        catch (Exception ex)
        {
            RecorderMod.Report("EventTap.OnCombatEnded", ex);
        }
    }

    private static void OnHistoryChanged()
    {
        try
        {
            var session = RecorderMod.Session;
            var entries = CombatManager.Instance.History.Entries as IList<CombatHistoryEntry>
                ?? CombatManager.Instance.History.Entries.ToList();
            var count = entries.Count;
            if (count < _lastSeenCount)
            {
                _lastSeenCount = 0; // Clear() at combat teardown; resync.
            }
            if (session == null)
            {
                _lastSeenCount = count;
                return;
            }
            for (var i = _lastSeenCount; i < count; i++)
            {
                var entry = entries[i];
                session.RecordEvent(EntryName(entry), Serialize(entry));
            }
            _lastSeenCount = count;
        }
        catch (Exception ex)
        {
            RecorderMod.Report("EventTap.OnHistoryChanged", ex);
        }
    }

    private static void OnChecksumGenerated(NetChecksumData data, string context, NetFullCombatState state)
    {
        try
        {
            if (_checksumSession == null || RecorderMod.Session != _checksumSession)
            {
                return;
            }
            _checksumSession.RecordEvent("checksum", new JsonObject
            {
                ["id"] = (long)data.id,
                ["checksum"] = (long)data.checksum,
                ["context"] = context,
            });
        }
        catch (Exception ex)
        {
            RecorderMod.Report("EventTap.OnChecksumGenerated", ex);
        }
    }

    /// <summary>"CardPlayStartedEntry" -> "card_play_started".</summary>
    private static string EntryName(CombatHistoryEntry entry)
    {
        var name = entry.GetType().Name;
        const string suffix = "Entry";
        if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
        {
            name = name[..^suffix.Length];
        }
        return JsonDescribe.SnakeCase(name);
    }

    /// <summary>
    /// Entry-type switch over all 17 CombatHistoryEntry subclasses (hook map).
    /// Primitive-field snapshots taken now — never at combat end.
    /// </summary>
    private static JsonObject Serialize(CombatHistoryEntry entry)
    {
        var data = new JsonObject
        {
            ["actor"] = JsonDescribe.Try(() => JsonDescribe.Creature(entry.Actor)),
            ["round"] = JsonDescribe.Try<int?>(() => entry.RoundNumber),
            ["side"] = JsonDescribe.Try(() => entry.CurrentSide.ToString()),
        };
        try
        {
            switch (entry)
            {
                case CardPlayStartedEntry e:
                    data["card_play"] = JsonDescribe.CardPlay(e.CardPlay);
                    break;
                case CardPlayFinishedEntry e:
                    data["card_play"] = JsonDescribe.CardPlay(e.CardPlay);
                    data["was_ethereal"] = e.WasEthereal;
                    break;
                case CardDrawnEntry e:
                    data["card"] = JsonDescribe.Card(e.Card);
                    data["from_hand_draw"] = e.FromHandDraw;
                    break;
                case CardDiscardedEntry e:
                    data["card"] = JsonDescribe.Card(e.Card);
                    break;
                case CardExhaustedEntry e:
                    data["card"] = JsonDescribe.Card(e.Card);
                    break;
                case CardGeneratedEntry e:
                    data["card"] = JsonDescribe.Card(e.Card);
                    data["generated_by_player"] = e.GeneratedByPlayer;
                    break;
                case CardAfflictedEntry e:
                    data["card"] = JsonDescribe.Card(e.Card);
                    data["affliction"] = JsonDescribe.Model(e.Affliction);
                    break;
                case CreatureAttackedEntry e:
                {
                    var results = new JsonArray();
                    foreach (var result in e.DamageResults)
                    {
                        results.Add(JsonDescribe.Damage(result));
                    }
                    data["damage_results"] = results;
                    break;
                }
                case DamageReceivedEntry e:
                    data["result"] = JsonDescribe.Damage(e.Result);
                    data["dealer"] = JsonDescribe.Creature(e.Dealer);
                    data["card_source"] = JsonDescribe.Card(e.CardSource);
                    break;
                case BlockGainedEntry e:
                    data["amount"] = e.Amount;
                    data["card_play"] = JsonDescribe.CardPlay(e.CardPlay);
                    break;
                case EnergySpentEntry e:
                    data["amount"] = e.Amount;
                    break;
                case MonsterPerformedMoveEntry e:
                {
                    data["monster"] = JsonDescribe.Model(e.Monster);
                    data["move"] = JsonDescribe.Try(() => e.Move.Id);
                    if (e.Targets != null)
                    {
                        var targets = new JsonArray();
                        foreach (var target in e.Targets)
                        {
                            targets.Add(JsonDescribe.Creature(target));
                        }
                        data["targets"] = targets;
                    }
                    break;
                }
                case OrbChanneledEntry e:
                    data["orb"] = JsonDescribe.Model(e.Orb);
                    break;
                case PotionUsedEntry e:
                    data["potion"] = JsonDescribe.Model(e.Potion);
                    data["target"] = JsonDescribe.Creature(e.Target);
                    break;
                case PowerReceivedEntry e:
                    data["power"] = JsonDescribe.Model(e.Power);
                    data["amount"] = JsonDescribe.Try<decimal?>(() => e.Amount);
                    data["applier"] = JsonDescribe.Creature(e.Applier);
                    break;
                case StarsModifiedEntry e:
                    data["amount"] = e.Amount;
                    break;
                case SummonedEntry e:
                    data["amount"] = e.Amount;
                    break;
                default:
                    data["entry_type"] = entry.GetType().Name;
                    data["description"] = JsonDescribe.Try(() => entry.Description);
                    break;
            }
        }
        catch (Exception ex)
        {
            data["serialize_error"] = ex.GetType().Name + ": " + ex.Message;
        }
        return data;
    }
}
