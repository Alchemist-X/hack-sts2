namespace Sts2Sim.Core.Run;

public sealed class RunSim
{
    private readonly List<RunAction> _legalBuffer = new(32);
    private RunState _state;
    private RunLoopState _loop;
    private Pending? _pending;
    private CombatSim? _combatView;
    private CanonicalWriter? _hashScratch;

    private RunSim(RunState state, RunLoopState loop, Pending? pending = null)
    {
        _state = state;
        _loop = loop;
        _pending = pending;
    }

    public static RunSim NewRun(RunConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Begin(RunLoop.CreateForNewRun(config), false, null);
    }

    public static RunSim FromSave(SavedRun save, ImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(options);
        var imported = RunImporter.ImportState(save, options);
        var rs = imported.State;
        RunLoop.GenerateMapOnLoad(rs);
        return FromLoadedState(rs, RunImporter.CreatePreFinishedRoom(rs, imported.PreFinishedRoom));
    }

    public static RunSim FromLoadedState(RunState state, AbstractRoom? preFinishedRoom)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Begin(state, true, preFinishedRoom);
    }

    public RunPhase Phase
    {
        get
        {
            if (_pending == null)
            {
                return _loop.Phase;
            }
            return _state.Combat is { IsInProgress: true } ? RunPhase.Combat : RunPhase.Prompt;
        }
    }

    public bool IsTerminal => _pending == null && _loop.Phase is RunPhase.Victory or RunPhase.Defeat;

    public RunState State => _state;

    public RunLoopState Loop => _loop;

    public CombatSim? Combat
    {
        get
        {
            var cs = _state.Combat;
            if (cs == null)
            {
                _combatView = null;
                return null;
            }
            if (_combatView == null || !ReferenceEquals(_combatView.State, cs))
            {
                _combatView = CombatSim.FromState(cs);
            }
            return _combatView;
        }
    }

    public ChoicePrompt? PendingChoice => _pending?.Prompt;

    public IReadOnlyList<int> PendingPicks => (IReadOnlyList<int>?)_pending?.Picks ?? Array.Empty<int>();

    public bool HasPendingChoice => _pending != null;

    public IReadOnlyList<RunAction> LegalActions()
    {
        FillLegal(_legalBuffer);
        return _legalBuffer;
    }

    public void LegalActions(List<RunAction> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        FillLegal(buffer);
    }

    public bool IsLegal(RunAction action)
    {
        if (IsTerminal)
        {
            return false;
        }
        var pending = _pending;
        return RunLegalActionGenerator.IsLegal(_state, _loop, pending?.Prompt, (IReadOnlyList<int>?)pending?.Picks ?? Array.Empty<int>(), action);
    }

    public void Step(RunAction action)
    {
        var pending = _pending;
        if (pending != null)
        {
            StepPrompt(pending, action, null);
            return;
        }
        RequireLegal(action);
        var basis = _state.DeepClone();
        var basisLoop = _loop.Clone();
        try
        {
            Execute(_state, _loop, action, null);
        }
        catch (ChoicePendingException ex)
        {
            _pending = new Pending(basis, basisLoop, action, new List<ChoiceAnswer>(), new List<ChoicePrompt>(), ex.Prompt, new List<int>());
        }
        catch
        {
            _state = basis;
            _loop = basisLoop;
            throw;
        }
    }

    public void Step(RunAction action, IChoiceAnswerer answerer)
    {
        ArgumentNullException.ThrowIfNull(answerer);
        var pending = _pending;
        if (pending != null)
        {
            StepPrompt(pending, action, answerer);
            return;
        }
        RequireLegal(action);
        Execute(_state, _loop, action, answerer);
    }

    public RunSim Clone() => new(_state.DeepClone(), _loop.Clone(), _pending?.Clone());

    /// <summary>
    /// A deep clone whose public state is identical and whose hidden state is resampled from <paramref name="seed"/>
    /// (draw-pile order, every RNG stream, pre-rolled run queues; see <see cref="Determinizer"/>). Same state and seed ⇒
    /// same clone (equal <see cref="StateHash"/>). At a pending prompt the prompt and its answer tape are kept: the
    /// clone's re-runs replay the recorded prefix unchanged and resample when the tape hands out the answer to the
    /// current prompt, so the tape always matches; the clone's current state is resampled as well.
    /// </summary>
    public RunSim CloneDeterminized(ulong seed, DeterminizeOptions? options = null)
    {
        var opts = options ?? DeterminizeOptions.Default;
        var clone = Clone();
        Determinizer.Apply(clone._state, clone._state.Combat, seed, opts);
        if (clone._pending is { } pending)
        {
            pending.Hooks = DeterminizeHook.Append(pending.Hooks, new DeterminizeHook(pending.Answers.Count, seed, opts));
        }
        return clone;
    }

    public ulong StateHash()
    {
        var w = _hashScratch ??= new CanonicalWriter();
        w.Reset();
        WriteCanonical(w);
        return StateHasher.Hash(w.WrittenSpan);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        ArgumentNullException.ThrowIfNull(w);
        w.WriteByte((byte)Phase);
        var pending = _pending;
        if (pending == null)
        {
            _loop.WriteCanonical(w);
            _state.WriteCanonical(w);
            return;
        }
        w.WriteTag(RunLoopCanonicalTags.Pending);
        pending.BasisLoop.WriteCanonical(w);
        pending.Basis.WriteCanonical(w);
        w.WriteTag(RunLoopCanonicalTags.PendingAction);
        w.WriteBool(pending.Action.HasValue);
        if (pending.Action is { } a)
        {
            w.WriteByte((byte)a.Kind);
            w.WriteInt32(a.A);
            w.WriteInt32(a.B);
        }
        w.WriteTag(RunLoopCanonicalTags.Tape);
        w.WriteCount(pending.Answers.Count);
        for (int i = 0; i < pending.Answers.Count; i++)
        {
            var answer = pending.Answers[i];
            w.WriteBool(answer.Cancelled);
            w.WriteCount(answer.Picks.Count);
            for (int j = 0; j < answer.Picks.Count; j++)
            {
                w.WriteInt32(answer.Picks[j]);
            }
        }
        w.WriteTag(RunLoopCanonicalTags.Picks);
        w.WriteCount(pending.Picks.Count);
        for (int i = 0; i < pending.Picks.Count; i++)
        {
            w.WriteInt32(pending.Picks[i]);
        }
        DeterminizeHook.Write(w, RunLoopCanonicalTags.Determinize, pending.Hooks);
    }

    private static RunSim Begin(RunState rs, bool isLoad, AbstractRoom? load)
    {
        var loop = new RunLoopState();
        var basis = rs.DeepClone();
        var basisLoop = loop.Clone();
        var sim = new RunSim(rs, loop);
        try
        {
            ExecuteBegin(rs, loop, isLoad, load, null);
        }
        catch (ChoicePendingException ex)
        {
            sim._pending = new Pending(basis, basisLoop, null, new List<ChoiceAnswer>(), new List<ChoicePrompt>(), ex.Prompt, new List<int>()) { IsLoad = isLoad, Load = load };
        }
        return sim;
    }

    private void FillLegal(List<RunAction> buffer)
    {
        buffer.Clear();
        if (IsTerminal)
        {
            return;
        }
        var pending = _pending;
        RunLegalActionGenerator.Fill(_state, _loop, pending?.Prompt, (IReadOnlyList<int>?)pending?.Picks ?? Array.Empty<int>(), buffer);
    }

    private void RequireLegal(RunAction action)
    {
        if (!IsLegal(action))
        {
            throw new InvalidOperationException($"Illegal action {action} in phase {Phase}.");
        }
    }

    private static void Execute(RunState rs, RunLoopState loop, RunAction action, IChoiceAnswerer? answerer)
    {
        var choice = rs.Choice;
        var previous = choice.Answerer;
        choice.Answerer = answerer;
        try
        {
            RunActionExecutor.Execute(rs, loop, action);
        }
        catch (ChoiceUnwindException) when (rs.Combat is { IsInProgress: false })
        {
            RunLoop.AfterAction(rs, loop);
        }
        finally
        {
            choice.Answerer = previous;
        }
    }

    private static void ExecuteBegin(RunState rs, RunLoopState loop, bool isLoad, AbstractRoom? load, IChoiceAnswerer? answerer)
    {
        var choice = rs.Choice;
        var previous = choice.Answerer;
        choice.Answerer = answerer;
        try
        {
            try
            {
                if (isLoad)
                {
                    RunLoop.LoadIntoLatestMapCoord(rs, load);
                }
                else
                {
                    RunLoop.Begin(rs, loop);
                }
            }
            catch (ChoiceUnwindException) when (rs.Combat is { IsInProgress: false })
            {
            }
            RunLoop.AfterAction(rs, loop);
        }
        finally
        {
            choice.Answerer = previous;
        }
    }

    private void StepPrompt(Pending pending, RunAction action, IChoiceAnswerer? answerer)
    {
        var prompt = pending.Prompt;
        if (!action.TryToCombat(out var ga) || RunAction.FromCombat(ga) != action || !LegalActionGenerator.TryResolvePrompt(prompt, pending.Picks, ga, out var error))
        {
            throw new InvalidOperationException($"Illegal action {action} at prompt {prompt.SiteId}.");
        }
        ChoiceAnswer answer;
        switch (ga.Kind)
        {
            case ActionKind.ChoosePick:
                if (!ChoiceRuntime.CompletesAutomatically(prompt, pending.Picks.Count + 1))
                {
                    pending.Picks.Add(ga.CardIndex);
                    return;
                }
                answer = Complete(prompt, pending.Picks, ga.CardIndex);
                break;
            case ActionKind.ChooseConfirm:
                answer = Complete(prompt, pending.Picks, null);
                break;
            default:
                answer = new ChoiceAnswer(Array.Empty<int>(), true);
                break;
        }
        Resume(pending, answer, answerer);
    }

    private void Resume(Pending pending, ChoiceAnswer answer, IChoiceAnswerer? answerer)
    {
        var answers = new List<ChoiceAnswer>(pending.Answers.Count + 1);
        answers.AddRange(pending.Answers);
        answers.Add(answer);
        var prompts = new List<ChoicePrompt>(pending.Prompts.Count + 1);
        prompts.AddRange(pending.Prompts);
        prompts.Add(pending.Prompt);
        var work = answerer == null ? pending.Basis.DeepClone() : pending.Basis;
        var workLoop = answerer == null ? pending.BasisLoop.Clone() : pending.BasisLoop;
        var hooks = pending.Hooks;
        var tape = new ChoiceTapeAnswerer(answers, prompts, answerer, hooks == null ? null : k => DeterminizeHook.Fire(hooks, k, work, work.Combat));
        try
        {
            if (pending.Action is { } a)
            {
                Execute(work, workLoop, a, tape);
            }
            else
            {
                ExecuteBegin(work, workLoop, pending.IsLoad, pending.Load, tape);
            }
        }
        catch (ChoicePendingException ex) when (answerer == null)
        {
            RequireTapeConsumed(tape, pending);
            _pending = new Pending(pending.Basis, pending.BasisLoop, pending.Action, answers, prompts, ex.Prompt, new List<int>()) { IsLoad = pending.IsLoad, Load = pending.Load, Hooks = hooks };
            _state = work;
            _loop = workLoop;
            return;
        }
        catch when (answerer != null)
        {
            _pending = null;
            _state = work;
            _loop = workLoop;
            throw;
        }
        RequireTapeConsumed(tape, pending);
        _pending = null;
        _state = work;
        _loop = workLoop;
    }

    private static ChoiceAnswer Complete(ChoicePrompt prompt, List<int> picks, int? last)
    {
        int count = picks.Count + (last.HasValue ? 1 : 0);
        var answer = new int[count];
        for (int i = 0; i < picks.Count; i++)
        {
            answer[i] = picks[i];
        }
        if (last is { } pick)
        {
            answer[count - 1] = pick;
        }
        if (!prompt.Ordered)
        {
            Array.Sort(answer);
        }
        return new ChoiceAnswer(answer, false);
    }

    private static void RequireTapeConsumed(ChoiceTapeAnswerer tape, Pending pending)
    {
        if (tape.Consumed != tape.Count)
        {
            throw new InvalidOperationException($"Choice tape divergence: the re-run of {(pending.Action is { } a ? a.ToString() : "Begin")} used {tape.Consumed} of {tape.Count} taped answers.");
        }
    }

    private sealed class Pending
    {
        public Pending(RunState basis, RunLoopState basisLoop, RunAction? action, List<ChoiceAnswer> answers, List<ChoicePrompt> prompts, ChoicePrompt prompt, List<int> picks)
        {
            Basis = basis;
            BasisLoop = basisLoop;
            Action = action;
            Answers = answers;
            Prompts = prompts;
            Prompt = prompt;
            Picks = picks;
        }

        public RunState Basis { get; }

        public RunLoopState BasisLoop { get; }

        public RunAction? Action { get; }

        public bool IsLoad { get; init; }

        public AbstractRoom? Load { get; init; }

        public List<ChoiceAnswer> Answers { get; }

        public List<ChoicePrompt> Prompts { get; }

        public ChoicePrompt Prompt { get; }

        public List<int> Picks { get; }

        /// <summary>Determinizations the re-runs apply (CloneDeterminized at a prompt); null for an ordinary pending prompt.</summary>
        public List<DeterminizeHook>? Hooks { get; set; }

        public Pending Clone()
        {
            var answers = new List<ChoiceAnswer>(Answers.Count);
            for (int i = 0; i < Answers.Count; i++)
            {
                answers.Add(ChoiceTape.Copy(Answers[i]));
            }
            var prompts = new List<ChoicePrompt>(Prompts.Count);
            for (int i = 0; i < Prompts.Count; i++)
            {
                prompts.Add(ChoiceTape.Copy(Prompts[i]));
            }
            return new Pending(Basis.DeepClone(), BasisLoop.Clone(), Action, answers, prompts, ChoiceTape.Copy(Prompt), new List<int>(Picks)) { IsLoad = IsLoad, Load = Load, Hooks = Hooks == null ? null : new List<DeterminizeHook>(Hooks) };
        }
    }
}
