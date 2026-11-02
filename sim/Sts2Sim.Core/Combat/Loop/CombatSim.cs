namespace Sts2Sim.Core;

public sealed class CombatSim
{
    private readonly List<GameAction> _legalBuffer = new(16);
    private CombatState _state;
    private Pending? _pending;
    private CanonicalWriter? _hashScratch;

    private CombatSim(CombatState state, Pending? pending = null)
    {
        _state = state;
        _pending = pending;
    }

    public static CombatSim Create(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0, int? currentHp = null, int? maxHp = null)
        => Begin(CombatLoop.SetUpCombat(new RunRngSet(seed), deckIds, encounterId, ascension, currentHp, maxHp));

    public static CombatSim Create(RunRngSet rng, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0, int? currentHp = null, int? maxHp = null)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Begin(CombatLoop.SetUpCombat(rng.DeepClone(), deckIds, encounterId, ascension, currentHp, maxHp));
    }

    public static CombatSim Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Begin(CombatLoop.SetUpCombat(rng.DeepClone(), deck, encounterId, ascension, currentHp, maxHp));
    }

    public static CombatSim Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp, CombatRunContext context)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Begin(CombatLoop.SetUpCombat(rng.DeepClone(), deck, encounterId, ascension, currentHp, maxHp, context));
    }

    public static CombatSim Create(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension, Action<CombatState> populateMonsters)
    {
        ArgumentNullException.ThrowIfNull(populateMonsters);
        return Begin(CombatLoop.SetUpCombat(seed, deckIds, encounterId, ascension, populateMonsters));
    }

    public static CombatSim FromSetUp(CombatState setUp)
    {
        ArgumentNullException.ThrowIfNull(setUp);
        if (!setUp.IsStarting || setUp.IsInProgress)
        {
            throw new InvalidOperationException("FromSetUp needs a combat that SetUpCombat prepared and that has not started yet.");
        }
        return Begin(setUp);
    }

    public static CombatSim FromState(CombatState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state);
    }

    public CombatState State => _state;

    public CombatOutcome Outcome => _pending != null ? CombatOutcome.InProgress : _state.Outcome;

    public bool IsTerminal => _pending == null && _state.Outcome != CombatOutcome.InProgress;

    public ChoicePrompt? PendingChoice => _pending?.Prompt;

    public IReadOnlyList<int> PendingPicks => (IReadOnlyList<int>?)_pending?.Picks ?? Array.Empty<int>();

    public bool HasPendingChoice => _pending != null;

    public bool PendingFromBegin => _pending != null && !_pending.Action.HasValue;

    public GameAction? PendingAction => _pending?.Action;

    public IReadOnlyList<ChoiceAnswer> PendingTape => (IReadOnlyList<ChoiceAnswer>?)_pending?.Answers ?? Array.Empty<ChoiceAnswer>();

    public IReadOnlyList<GameAction> LegalActions()
    {
        _legalBuffer.Clear();
        FillLegal(_legalBuffer);
        return _legalBuffer;
    }

    public void LegalActions(List<GameAction> buffer)
    {
        buffer.Clear();
        FillLegal(buffer);
    }

    public bool IsLegal(GameAction action)
    {
        var pending = _pending;
        return pending == null
            ? LegalActionGenerator.IsLegal(_state, action)
            : LegalActionGenerator.TryResolvePrompt(pending.Prompt, pending.Picks, action, out _);
    }

    public void Step(GameAction action)
    {
        var pending = _pending;
        if (pending != null)
        {
            StepPrompt(pending, action, null);
            return;
        }
        RequireLegal(_state, action);
        var basis = _state.DeepClone();
        try
        {
            Execute(_state, action, null);
        }
        catch (ChoicePendingException ex)
        {
            _pending = new Pending(basis, action, new List<ChoiceAnswer>(), new List<ChoicePrompt>(), ex.Prompt, new List<int>());
        }
        catch
        {
            _state = basis;
            throw;
        }
    }

    public void Step(GameAction action, IChoiceAnswerer answerer)
    {
        ArgumentNullException.ThrowIfNull(answerer);
        var pending = _pending;
        if (pending != null)
        {
            StepPrompt(pending, action, answerer);
            return;
        }
        RequireLegal(_state, action);
        Execute(_state, action, answerer);
    }

    public CombatSim Clone() => new(_state.DeepClone(), _pending?.Clone());

    /// <summary>
    /// A deep clone with identical public state and hidden state resampled from <paramref name="seed"/> (draw-pile
    /// order, every RNG stream; see <see cref="Determinizer"/>); prompts behave as in <c>RunSim.CloneDeterminized</c>.
    /// </summary>
    public CombatSim CloneDeterminized(ulong seed, DeterminizeOptions? options = null)
    {
        var opts = options ?? DeterminizeOptions.Default;
        var clone = Clone();
        Determinizer.Apply(clone._state.Run, clone._state, seed, opts);
        if (clone._pending is { } pending)
        {
            pending.Hooks = DeterminizeHook.Append(pending.Hooks, new DeterminizeHook(pending.Answers.Count, seed, opts));
        }
        return clone;
    }

    public ulong StateHash() => StateHash(_hashScratch ??= new CanonicalWriter());

    public ulong StateHash(CanonicalWriter scratch)
    {
        ArgumentNullException.ThrowIfNull(scratch);
        if (_pending == null)
        {
            return StateHasher.Hash(_state, scratch);
        }
        scratch.Reset();
        WriteCanonical(scratch);
        return StateHasher.Hash(scratch.WrittenSpan);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        var pending = _pending;
        if (pending == null)
        {
            _state.WriteCanonical(w);
        }
        else
        {
            ChoiceTape.WritePending(w, pending.Basis, pending.Action, pending.Answers, pending.Picks);
            DeterminizeHook.Write(w, ChoiceCanonicalTags.Determinize, pending.Hooks);
        }
    }

    private static CombatSim Begin(CombatState setUp)
    {
        var basis = setUp.DeepClone();
        var sim = new CombatSim(setUp);
        try
        {
            Execute(setUp, null, null);
        }
        catch (ChoicePendingException ex)
        {
            sim._pending = new Pending(basis, null, new List<ChoiceAnswer>(), new List<ChoicePrompt>(), ex.Prompt, new List<int>());
        }
        return sim;
    }

    private void FillLegal(List<GameAction> buffer)
    {
        var pending = _pending;
        if (pending == null)
        {
            LegalActionGenerator.Fill(_state, buffer);
        }
        else
        {
            LegalActionGenerator.FillPrompt(pending.Prompt, pending.Picks, buffer);
        }
    }

    private void StepPrompt(Pending pending, GameAction action, IChoiceAnswerer? answerer)
    {
        var prompt = pending.Prompt;
        if (!LegalActionGenerator.TryResolvePrompt(prompt, pending.Picks, action, out var error))
        {
            throw new InvalidOperationException($"Illegal action {action}: {error}");
        }
        ChoiceAnswer answer;
        switch (action.Kind)
        {
            case ActionKind.ChoosePick:
                if (!ChoiceRuntime.CompletesAutomatically(prompt, pending.Picks.Count + 1))
                {
                    pending.Picks.Add(action.CardIndex);
                    return;
                }
                answer = Complete(prompt, pending.Picks, action.CardIndex);
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
        var hooks = pending.Hooks;
        var tape = new ChoiceTapeAnswerer(answers, prompts, answerer, hooks == null ? null : k => DeterminizeHook.Fire(hooks, k, work.Run, work));
        try
        {
            Execute(work, pending.Action, tape);
        }
        catch (ChoicePendingException ex) when (answerer == null)
        {
            RequireTapeConsumed(tape, pending);
            _pending = new Pending(pending.Basis, pending.Action, answers, prompts, ex.Prompt, new List<int>()) { Hooks = hooks };
            _state = work;
            return;
        }
        catch when (answerer != null)
        {
            _pending = null;
            _state = work;
            throw;
        }
        RequireTapeConsumed(tape, pending);
        _pending = null;
        _state = work;
    }

    private static void Execute(CombatState work, GameAction? action, IChoiceAnswerer? answerer)
    {
        var choice = work.Choice;
        var previous = choice.Answerer;
        choice.Answerer = answerer;
        try
        {
            if (action is { } a)
            {
                CombatLoop.Step(work, a);
            }
            else
            {
                CombatLoop.StartCombat(work);
            }
        }
        catch (ChoiceUnwindException) when (!work.IsInProgress)
        {
        }
        finally
        {
            choice.Answerer = previous;
        }
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

    private static void RequireLegal(CombatState cs, GameAction action)
    {
        if (!LegalActionGenerator.TryResolve(cs, action, out _, out var error))
        {
            throw new InvalidOperationException($"Illegal action {action}: {error}");
        }
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
        public Pending(CombatState basis, GameAction? action, List<ChoiceAnswer> answers, List<ChoicePrompt> prompts, ChoicePrompt prompt, List<int> picks)
        {
            Basis = basis;
            Action = action;
            Answers = answers;
            Prompts = prompts;
            Prompt = prompt;
            Picks = picks;
        }

        public CombatState Basis { get; }

        public GameAction? Action { get; }

        public List<ChoiceAnswer> Answers { get; }

        public List<ChoicePrompt> Prompts { get; }

        public ChoicePrompt Prompt { get; }

        public List<int> Picks { get; }

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
            return new Pending(Basis.DeepClone(), Action, answers, prompts, ChoiceTape.Copy(Prompt), new List<int>(Picks)) { Hooks = Hooks == null ? null : new List<DeterminizeHook>(Hooks) };
        }
    }
}
