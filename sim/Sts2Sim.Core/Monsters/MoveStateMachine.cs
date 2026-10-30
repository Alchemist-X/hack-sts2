using System.Diagnostics.CodeAnalysis;

namespace Sts2Sim.Core.Monsters;

public delegate void MovePerform(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets);

public abstract class AiStateDef
{
    public abstract string Id { get; }

    public virtual bool ShouldAppearInLogs => true;

    public virtual bool IsMove => false;

    public virtual bool CanTransitionAway(MonsterAiRuntime runtime) => true;

    public abstract string? GetNextState(CombatState cs, MonsterState owner, Rng rng);

    internal virtual string? ResolveNextState(CombatState cs, MonsterState owner, MonsterAiRuntime runtime, Rng rng)
        => GetNextState(cs, owner, rng);
}

public sealed class MoveStateDef : AiStateDef
{
    public const string UnsetMoveId = "UNSET_MOVE";

    public const string StunnedMoveId = "STUNNED";

    public MoveStateDef(string stateId, MovePerform perform, params AbstractIntent[] intents)
    {
        StateId = stateId;
        Perform = perform;
        Intents = intents;
    }

    public string StateId { get; }

    public override string Id => StateId;

    public override bool IsMove => true;

    public MovePerform Perform { get; }

    public IReadOnlyList<AbstractIntent> Intents { get; }

    public bool MustPerformOnceBeforeTransitioning { get; set; }

    public string? FollowUpStateId { get; init; }

    public AiStateDef? FollowUpState { get; set; }

    public bool IntendsToAttack
    {
        get
        {
            var intents = Intents;
            for (int i = 0; i < intents.Count; i++)
            {
                var type = intents[i].IntentType;
                if (type == IntentType.Attack || type == IntentType.DeathBlow)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public override bool CanTransitionAway(MonsterAiRuntime runtime)
    {
        if (MustPerformOnceBeforeTransitioning)
        {
            return runtime.HasPerformedAtLeastOnce(this);
        }
        return true;
    }

    public override string? GetNextState(CombatState cs, MonsterState owner, Rng rng)
        => (FollowUpState?.Id ?? FollowUpStateId) ?? throw new InvalidOperationException("No valid followup state.");
}

public sealed class RandomBranchStateDef : AiStateDef
{
    public readonly record struct StateWeight(string StateId, MoveRepeatType RepeatType, int MaxTimes, Func<CombatState, MonsterState, float> Weight, int Cooldown)
    {
        public float GetWeight(CombatState cs, MonsterState owner)
        {
            if (Weight != null)
            {
                return Weight(cs, owner);
            }
            throw new InvalidOperationException(StateId + " doesn't have a weight");
        }
    }

    public RandomBranchStateDef(string stateId)
    {
        StateId = stateId;
    }

    public string StateId { get; }

    public override string Id => StateId;

    public override bool ShouldAppearInLogs => false;

    public List<StateWeight> States { get; } = new();

    public void AddBranch(AiStateDef state, int cooldown, MoveRepeatType repeatType, Func<CombatState, MonsterState, float> weight)
    {
        if (repeatType == MoveRepeatType.CanRepeatXTimes)
        {
            throw new ArgumentException("Use other constructor to specify number of repeats");
        }
        States.Add(new StateWeight(state.Id, repeatType, 0, weight, cooldown));
    }

    public void AddBranch(AiStateDef state, int cooldown, int maxRepeats, Func<CombatState, MonsterState, float> weight)
    {
        States.Add(new StateWeight(state.Id, MoveRepeatType.CanRepeatXTimes, maxRepeats, weight, cooldown));
    }

    public void AddBranch(AiStateDef state, int maxRepeats, Func<CombatState, MonsterState, float> weight)
        => AddBranch(state, 0, maxRepeats, weight);

    public void AddBranch(AiStateDef state, int cooldown, MoveRepeatType repeatType, float weight)
        => AddBranch(state, cooldown, repeatType, Constant(weight));

    public void AddBranch(AiStateDef state, MoveRepeatType repeatType, Func<CombatState, MonsterState, float> weight)
        => AddBranch(state, 0, repeatType, weight);

    public void AddBranch(AiStateDef state, MoveRepeatType repeatType, float weight = 1f)
        => AddBranch(state, repeatType, Constant(weight));

    public void AddBranch(AiStateDef state, int maxRepeats, float weight = 1f)
        => AddBranch(state, maxRepeats, Constant(weight));

    public void AddBranch(AiStateDef state, int cooldown, MoveRepeatType repeatType)
        => AddBranch(state, cooldown, repeatType, 1f);

    public override string? GetNextState(CombatState cs, MonsterState owner, Rng rng)
        => ResolveNextState(cs, owner, owner.Ai, rng);

    internal override string? ResolveNextState(CombatState cs, MonsterState owner, MonsterAiRuntime runtime, Rng rng)
    {
        var states = States;
        double sum = 0.0;
        for (int i = 0; i < states.Count; i++)
        {
            sum += GetStateWeight(cs, states[i], owner, runtime);
        }
        float max = (float)sum;
        float num = rng.NextFloat(max);
        for (int i = 0; i < states.Count; i++)
        {
            var current = states[i];
            num -= GetStateWeight(cs, current, owner, runtime);
            if (num <= 0f)
            {
                return current.StateId;
            }
        }
        throw new InvalidOperationException("No valid state found in RandomBranchState " + Id + "!");
    }

    public static float GetStateWeight(CombatState cs, StateWeight stateWeight, MonsterState owner, MonsterAiRuntime runtime)
    {
        var machine = runtime.Machine;
        var log = runtime.StateLog;
        float num = 1f;
        if (stateWeight.RepeatType == MoveRepeatType.UseOnlyOnce)
        {
            var target = machine.Get(stateWeight.StateId);
            if (log.Contains(target.Id))
            {
                num = 0f;
            }
        }
        else if (stateWeight.RepeatType != MoveRepeatType.CanRepeatForever)
        {
            float num2 = stateWeight.RepeatType == MoveRepeatType.CannotRepeat ? 1 : stateWeight.MaxTimes;
            num = ((float)log.Count < num2) ? 1 : 0;
            int num3 = 0;
            while ((float)log.Count >= num2 && (float)num3 < num2 && log.Count - num3 > 0)
            {
                var target = machine.Get(stateWeight.StateId);
                if (!string.Equals(log[log.Count - 1 - num3], target.Id, StringComparison.Ordinal))
                {
                    num = 1f;
                    break;
                }
                num3++;
            }
        }
        if (stateWeight.Cooldown > 0)
        {
            int taken = 0;
            for (int i = log.Count - 1; i >= 0 && taken < stateWeight.Cooldown; i--)
            {
                if (!runtime.IsMoveId(log[i]))
                {
                    continue;
                }
                taken++;
                if (string.Equals(log[i], stateWeight.StateId, StringComparison.Ordinal))
                {
                    return 0f;
                }
            }
        }
        return num * stateWeight.GetWeight(cs, owner);
    }

    private static Func<CombatState, MonsterState, float> Constant(float weight) => (_, _) => weight;
}

public sealed class ConditionalBranchStateDef : AiStateDef
{
    public readonly record struct ConditionalBranch(string StateId, Func<CombatState, MonsterState, bool>? Condition)
    {
        public float Evaluate(CombatState cs, MonsterState owner)
        {
            if (Condition != null)
            {
                return Condition(cs, owner) ? 1 : 0;
            }
            return 1f;
        }
    }

    public ConditionalBranchStateDef(string stateId)
    {
        BranchId = stateId;
    }

    public string BranchId { get; }

    public override string Id => BranchId;

    public override bool ShouldAppearInLogs => false;

    public List<ConditionalBranch> States { get; } = new();

    public void AddState(AiStateDef move, Func<CombatState, MonsterState, bool>? condition)
        => States.Add(new ConditionalBranch(move.Id, condition));

    public override string? GetNextState(CombatState cs, MonsterState owner, Rng rng)
    {
        var states = States;
        for (int i = 0; i < states.Count; i++)
        {
            var current = states[i];
            if (current.Evaluate(cs, owner) > 0f)
            {
                return current.StateId;
            }
        }
        throw new InvalidOperationException("No valid next state found.");
    }
}

public sealed class MonsterMoveStateMachineDef
{
    private readonly Dictionary<string, AiStateDef> _states;

    public MonsterMoveStateMachineDef(IEnumerable<AiStateDef> states, AiStateDef initialState)
    {
        _states = new Dictionary<string, AiStateDef>(StringComparer.Ordinal);
        foreach (var s in states)
        {
            _states.Add(s.Id, s);
        }
        InitialState = initialState;
    }

    public IReadOnlyDictionary<string, AiStateDef> States => _states;

    public AiStateDef InitialState { get; }

    public string InitialStateId => InitialState.Id;

    public AiStateDef Get(string id) => _states[id];

    public bool TryGet(string id, [MaybeNullWhen(false)] out AiStateDef state) => _states.TryGetValue(id, out state);

    public bool Contains(string id) => _states.ContainsKey(id);
}

public sealed class MonsterAiRuntime : ICanonical
{
    private AiStateDef _current;

    public MonsterAiRuntime(MonsterMoveStateMachineDef machine)
    {
        Machine = machine;
        _current = machine.InitialState;
        StateLog = new List<string>(8);
        PerformedAtLeastOnce = new HashSet<string>(StringComparer.Ordinal);
        if (_current.ShouldAppearInLogs)
        {
            StateLog.Add(_current.Id);
        }
    }

    private MonsterAiRuntime(MonsterAiRuntime source)
    {
        Machine = source.Machine;
        _current = source._current;
        PerformedFirstMove = source.PerformedFirstMove;
        StateLog = new List<string>(Math.Max(8, source.StateLog.Count + 4));
        StateLog.AddRange(source.StateLog);
        PerformedAtLeastOnce = source.PerformedAtLeastOnce.Count == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(source.PerformedAtLeastOnce, StringComparer.Ordinal);
        NextMove = source.NextMove;
        IsPerformingMove = source.IsPerformingMove;
    }

    public MonsterMoveStateMachineDef Machine { get; }

    public AiStateDef CurrentState => _current;

    public string CurrentStateId
    {
        get => _current.Id;
        set => _current = Machine.Get(value);
    }

    public bool PerformedFirstMove { get; set; }

    public List<string> StateLog { get; }

    public HashSet<string> PerformedAtLeastOnce { get; }

    public MoveStateDef? NextMove { get; set; }

    public bool IsPerformingMove { get; set; }

    public bool IntendsToAttack => NextMove != null && NextMove.IntendsToAttack;

    public bool HasPerformedAtLeastOnce(MoveStateDef state) => PerformedAtLeastOnce.Contains(state.Id);

    public void MarkPerformedAtLeastOnce(MoveStateDef move) => PerformedAtLeastOnce.Add(move.Id);

    public MoveStateDef RollMove(CombatState cs, MonsterState owner, Rng rng)
    {
        FindNextMoveState(cs, owner, rng, logMove: true);
        if (!_current.IsMove)
        {
            throw new InvalidOperationException(_current.Id + " is not a valid move state");
        }
        var move = (MoveStateDef)_current;
        NextMove = move;
        return move;
    }

    public void ForceCurrentState(string stateId) => SetCurrentState(Machine.Get(stateId));

    public void ForceCurrentState(AiStateDef state) => SetCurrentState(state);

    public void OnMovePerformed(MoveStateDef move) => PerformedFirstMove = true;

    public void SetMoveImmediate(MoveStateDef state, bool forceTransition = false)
    {
        if (NextMove == null || NextMove.CanTransitionAway(this) || forceTransition)
        {
            NextMove = state;
            ForceCurrentState(state);
        }
    }

    public bool IsMoveId(string stateId)
    {
        if (Machine.TryGet(stateId, out var state))
        {
            return state.IsMove;
        }
        if (string.Equals(Machine.InitialState.Id, stateId, StringComparison.Ordinal))
        {
            return Machine.InitialState.IsMove;
        }
        return false;
    }

    private void FindNextMoveState(CombatState cs, MonsterState owner, Rng rng, bool logMove)
    {
        if (!_current.CanTransitionAway(this) || (!PerformedFirstMove && _current.IsMove))
        {
            return;
        }
        string? logged = null;
        do
        {
            string? nextState = _current.ResolveNextState(cs, owner, this, rng);
            AiStateDef next;
            if (string.IsNullOrEmpty(nextState))
            {
                next = Machine.InitialState;
            }
            else
            {
                if (!Machine.TryGet(nextState, out var found))
                {
                    throw new InvalidOperationException("no valid state found: " + nextState);
                }
                next = found;
            }
            SetCurrentState(next);
            if (logged == null && _current.ShouldAppearInLogs)
            {
                logged = _current.Id;
            }
        }
        while (!_current.IsMove);
        if (logMove && logged != null)
        {
            StateLog.Add(logged);
        }
    }

    private void SetCurrentState(AiStateDef state)
    {
        if (_current is MoveStateDef exited)
        {
            PerformedAtLeastOnce.Remove(exited.Id);
        }
        _current = state;
    }

    public MonsterAiRuntime DeepClone() => new(this);

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(MonsterCanonicalTags.AiRuntime);
        w.WriteString(Machine.InitialState.Id);
        w.WriteCount(Machine.States.Count);
        WriteStateRef(w, _current);
        w.WriteBool(PerformedFirstMove);
        var log = StateLog;
        w.WriteCount(log.Count);
        for (int i = 0; i < log.Count; i++)
        {
            w.WriteString(log[i]);
        }
        WritePerformedSet(w);
        w.WriteBool(NextMove != null);
        if (NextMove != null)
        {
            w.WriteBool(ReferenceEquals(NextMove, _current));
            WriteStateRef(w, NextMove);
        }
        w.WriteBool(IsPerformingMove);
    }

    private void WriteStateRef(CanonicalWriter w, AiStateDef state)
    {
        w.WriteTag(MonsterCanonicalTags.AiStateRef);
        w.WriteString(state.Id);
        bool registered = Machine.TryGet(state.Id, out var registeredState) && ReferenceEquals(registeredState, state);
        w.WriteBool(registered);
        if (!registered && state is MoveStateDef move)
        {
            w.WriteString(move.FollowUpState?.Id ?? move.FollowUpStateId);
            w.WriteBool(move.MustPerformOnceBeforeTransitioning);
            w.WriteCount(move.Intents.Count);
            for (int i = 0; i < move.Intents.Count; i++)
            {
                w.WriteInt32((int)move.Intents[i].IntentType);
            }
        }
    }

    private void WritePerformedSet(CanonicalWriter w)
    {
        var set = PerformedAtLeastOnce;
        w.WriteCount(set.Count);
        if (set.Count == 0)
        {
            return;
        }
        if (set.Count == 1)
        {
            foreach (var id in set)
            {
                w.WriteString(id);
            }
            return;
        }
        var sorted = new string[set.Count];
        set.CopyTo(sorted);
        Array.Sort(sorted, StringComparer.Ordinal);
        for (int i = 0; i < sorted.Length; i++)
        {
            w.WriteString(sorted[i]);
        }
    }
}
