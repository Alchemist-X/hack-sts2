using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>A pre-input observation pinned to its recording session, not to LatestStateSeq.</summary>
public sealed record DecisionCapture(TrajectorySession Session, long StateSeq, string Stage)
{
    public JsonObject Describe() => new()
    {
        ["status"] = StateSeq > 0 ? "captured" : "missing",
        ["stage"] = Stage,
    };

    public long Record(TrajectorySession? current, string source, string kind,
        JsonObject parameters, string status = "committed")
    {
        // A delayed continuation from a closed run must never land in the next run.
        if (!ReferenceEquals(current, Session)) return 0;
        parameters["_decision"] = Describe();
        // Explicit 0 means missing. null would silently fall back to the latest state.
        return Session.RecordAction(source, kind, parameters, status, StateSeq);
    }
}

/// <summary>
/// Main-thread choice correlation. ReserveChoiceId precedes the UI await; bind that
/// await's screen instance to the reserved ID, then retain its latest pre-input
/// capture until SyncLocalChoice consumes that exact ID. Nested choices, rerolls
/// on the same screen and unrelated snapshots cannot overwrite another choice.
/// </summary>
public sealed class DecisionChoices
{
    private readonly Dictionary<(ulong Player, uint Id), DecisionCapture?> _captures = new();
    private readonly List<(ulong Player, uint Id)> _unbound = new();
    private readonly Dictionary<object, (ulong Player, uint Id)> _owners =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, object> _bindings = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _activeInputs = new(ReferenceEqualityComparer.Instance);

    // Only coalesce synchronous nesting for the exact same binding. Never use a
    // frame/time window: a second click, nested screen or reroll must capture again.
    public sealed class InputScope : IDisposable
    {
        private readonly DecisionChoices _registry;
        internal readonly object Binding;
        public bool ShouldCapture { get; }
        private bool _disposed;
        internal InputScope(DecisionChoices registry, object binding, bool capture)
            => (_registry, Binding, ShouldCapture) = (registry, binding, capture);
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (ShouldCapture) _registry._activeInputs.Remove(Binding);
        }
    }

    public InputScope? BeginInput(object owner)
    {
        if (!_bindings.TryGetValue(owner, out var binding)) return null;
        return new InputScope(this, binding, _activeInputs.Add(binding));
    }

    public bool IsCurrent(object owner, InputScope input) =>
        _bindings.TryGetValue(owner, out var binding) && ReferenceEquals(binding, input.Binding);

    public void Reserve(ulong player, uint id)
    {
        var key = (player, id);
        _captures[key] = null;
        _unbound.Remove(key);
        _unbound.Add(key);
    }

    public bool Bind(object owner, ulong player)
    {
        for (int i = _unbound.Count - 1; i >= 0; i--)
        {
            var key = _unbound[i];
            if (key.Player != player) continue;
            _unbound.RemoveAt(i);
            _owners[owner] = key;
            _bindings[owner] = new object();
            return true;
        }
        return false;
    }

    public bool Update(object owner, DecisionCapture capture)
    {
        if (!_owners.TryGetValue(owner, out var key) || !_captures.ContainsKey(key)) return false;
        _captures[key] = capture;
        return true;
    }

    public bool IsBound(object owner) => _owners.ContainsKey(owner);

    public DecisionCapture? Consume(ulong player, uint id)
    {
        var key = (player, id);
        _captures.Remove(key, out var capture);
        _unbound.Remove(key);
        var owners = new List<object>();
        foreach (var pair in _owners)
            if (pair.Value == key) owners.Add(pair.Key);
        foreach (var owner in owners)
        {
            _owners.Remove(owner);
            _bindings.Remove(owner);
        }
        return capture;
    }
}
