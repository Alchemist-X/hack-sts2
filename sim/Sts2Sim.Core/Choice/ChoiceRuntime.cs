namespace Sts2Sim.Core.Choice;

public readonly struct ContextScope : IDisposable
{
    private readonly ChoiceRuntime? _runtime;
    private readonly int _depth;

    internal ContextScope(ChoiceRuntime runtime, int depth)
    {
        _runtime = runtime;
        _depth = depth;
    }

    public int Depth => _depth;

    public bool FirstPauseDone => _runtime != null && _runtime.FirstPauseDoneAt(_depth);

    public void Dispose() => _runtime?.PopContext(_depth);
}

public readonly record struct ChoiceContextFrame(ChoiceContextKind Kind, Action? OnFirstPause, bool FirstPauseDone);

public sealed class ChoiceRuntime : ICanonical
{
    private const int InitialContextCapacity = 4;

    private readonly List<IChoiceAnswerer> _selectors;
    private List<ChoiceContextFrame>? _contexts;

    public ChoiceRuntime()
        : this(new List<IChoiceAnswerer>())
    {
    }

    private ChoiceRuntime(List<IChoiceAnswerer> selectors)
    {
        _selectors = selectors;
    }

    public IChoiceAnswerer? Answerer { get; set; }

    public IReadOnlyList<IChoiceAnswerer> Selectors => _selectors;

    public bool HasSelector => _selectors.Count > 0;

    public IChoiceAnswerer? Selector => _selectors.Count > 0 ? _selectors[_selectors.Count - 1] : null;

    public IReadOnlyList<ChoiceContextFrame> Contexts => (IReadOnlyList<ChoiceContextFrame>?)_contexts ?? Array.Empty<ChoiceContextFrame>();

    public int ContextDepth => _contexts?.Count ?? 0;

    public ChoiceContextKind CurrentContextKind
    {
        get
        {
            var contexts = _contexts;
            return contexts is { Count: > 0 } ? contexts[contexts.Count - 1].Kind : ChoiceContextKind.Inline;
        }
    }

    public ContextScope PushContext(ChoiceContextKind kind, Action? onFirstPause = null)
    {
        if (kind is < ChoiceContextKind.Inline or > ChoiceContextKind.Throwing)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown choice context kind.");
        }
        if (onFirstPause != null && kind != ChoiceContextKind.TurnSetup)
        {
            throw new ArgumentException($"Only a {ChoiceContextKind.TurnSetup} context runs steps at its first pause; a {kind} context takes no callback.", nameof(onFirstPause));
        }
        var contexts = _contexts ??= new List<ChoiceContextFrame>(InitialContextCapacity);
        int depth = contexts.Count;
        contexts.Add(new ChoiceContextFrame(kind, onFirstPause, false));
        return new ContextScope(this, depth);
    }

    internal void PopContext(int depth)
    {
        int count = ContextDepth;
        if (depth != count - 1)
        {
            throw new InvalidOperationException($"Choice context {depth} popped out of order: the context stack holds {count} contexts.");
        }
        _contexts!.RemoveAt(depth);
    }

    internal bool FirstPauseDoneAt(int depth)
    {
        var contexts = _contexts;
        return contexts != null && (uint)depth < (uint)contexts.Count && contexts[depth].FirstPauseDone;
    }

    public void SignalPlayerChoiceBegun(CombatState cs, string siteId)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(siteId);
        var contexts = _contexts;
        if (contexts == null || contexts.Count == 0)
        {
            return;
        }
        int top = contexts.Count - 1;
        var frame = contexts[top];
        switch (frame.Kind)
        {
            case ChoiceContextKind.Inline:
                return;
            case ChoiceContextKind.TurnSetup:
                if (frame.FirstPauseDone)
                {
                    return;
                }
                contexts[top] = frame with { FirstPauseDone = true };
                if (frame.OnFirstPause != null)
                {
                    RunFirstPauseSteps(cs, contexts, top, frame.OnFirstPause, siteId);
                }
                return;
            case ChoiceContextKind.PerListener:
                if (frame.FirstPauseDone)
                {
                    return;
                }
                contexts[top] = frame with { FirstPauseDone = true };
                cs.Run.Fidelity.Record(FidelityFlag.DeferredListenerPromptInline, siteId);
                return;
            case ChoiceContextKind.Throwing:
                throw new InvalidOperationException($"{siteId} asked for a player choice inside a {ChoiceContextKind.Throwing} choice context (a command issued by monster code); no choice can be made there.");
            default:
                throw new InvalidOperationException($"Unknown choice context kind {frame.Kind}.");
        }
    }

    private static void RunFirstPauseSteps(CombatState cs, List<ChoiceContextFrame> contexts, int root, Action steps, string siteId)
    {
        var hidden = contexts.GetRange(root, contexts.Count - root);
        contexts.RemoveRange(root, contexts.Count - root);
        bool wasInProgress = cs.IsInProgress;
        try
        {
            steps();
        }
        catch
        {
            Restore(contexts, root, hidden);
            throw;
        }
        bool balanced = contexts.Count == root;
        Restore(contexts, root, hidden);
        if (!balanced)
        {
            throw new InvalidOperationException($"The first-pause steps of the turn-setup context paused by {siteId} left the choice context stack unbalanced.");
        }
        if (wasInProgress && !cs.IsInProgress)
        {
            throw new ChoiceUnwindException(siteId);
        }
    }

    private static void Restore(List<ChoiceContextFrame> contexts, int root, List<ChoiceContextFrame> hidden)
    {
        if (contexts.Count > root)
        {
            contexts.RemoveRange(root, contexts.Count - root);
        }
        contexts.AddRange(hidden);
    }

    public void PushSelector(IChoiceAnswerer selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (selector is not IChoiceSelector named || string.IsNullOrEmpty(named.SelectorId))
        {
            throw new ArgumentException($"A selector on the choice runtime must implement {nameof(IChoiceSelector)} with a non-empty stable SelectorId (it is written to the canonical form).", nameof(selector));
        }
        if (selector is ICanonical && selector is not IStatefulChoiceSelector)
        {
            throw new ArgumentException($"Selector {named.SelectorId} has canonical state but does not implement {nameof(IStatefulChoiceSelector)}, so a clone would share it.", nameof(selector));
        }
        _selectors.Add(selector);
    }

    public SelectorScope PushSelectorScope(IChoiceAnswerer selector)
    {
        PushSelector(selector);
        return new SelectorScope(this, selector);
    }

    public void PopSelector()
    {
        if (_selectors.Count == 0)
        {
            throw new InvalidOperationException("No card selector to pop.");
        }
        _selectors.RemoveAt(_selectors.Count - 1);
    }

    public bool PopSelector(IChoiceAnswerer selector)
    {
        var selectors = _selectors;
        if (selectors.Count > 0 && ReferenceEquals(selectors[selectors.Count - 1], selector))
        {
            selectors.RemoveAt(selectors.Count - 1);
            return true;
        }
        return false;
    }

    public ChoiceAnswer Ask(ChoicePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ValidatePrompt(prompt);
        var selectors = _selectors;
        if (selectors.Count > 0)
        {
            var selector = selectors[selectors.Count - 1];
            return NormalizeSelectorAnswer(prompt, selector.Answer(prompt), selector);
        }
        var answerer = Answerer;
        if (answerer == null)
        {
            throw new ChoicePendingException(prompt);
        }
        return NormalizeAnswer(prompt, answerer.Answer(prompt));
    }

    public static bool CompletesAutomatically(ChoicePrompt prompt, int pickCount) => !prompt.RequireConfirm && pickCount == prompt.MaxSelect;

    public static bool CanConfirm(ChoicePrompt prompt, int pickCount)
        => (prompt.RequireConfirm || prompt.CanSkip) && prompt.MinSelect <= pickCount && pickCount <= prompt.MaxSelect;

    public static void ValidatePrompt(ChoicePrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.SiteId is null || prompt.SourceId is null)
        {
            throw new ArgumentException("A choice prompt needs a site id and a source id.", nameof(prompt));
        }
        var options = prompt.Options ?? throw new ArgumentException($"Prompt {prompt.SiteId} has no option list.", nameof(prompt));
        int n = options.Count;
        if (n == 0)
        {
            throw new ArgumentException($"Prompt {prompt.SiteId} has no options; the selection resolves without a prompt.", nameof(prompt));
        }
        if (prompt.MinSelect < 0 || prompt.MinSelect > prompt.MaxSelect || prompt.MaxSelect > n)
        {
            throw new ArgumentException($"Prompt {prompt.SiteId} has selection bounds {prompt.MinSelect}..{prompt.MaxSelect} for {n} options.", nameof(prompt));
        }
        if (CompletesAutomatically(prompt, 0))
        {
            throw new ArgumentException($"Prompt {prompt.SiteId} is complete with no pick (MaxSelect 0 without confirmation); the caller resolves it without asking.", nameof(prompt));
        }
    }

    private static ChoiceAnswer NormalizeAnswer(ChoicePrompt prompt, ChoiceAnswer answer)
    {
        var picks = answer.Picks ?? Array.Empty<int>();
        if (answer.Cancelled)
        {
            if (!prompt.Cancelable)
            {
                throw new InvalidOperationException($"Prompt {prompt.SiteId} cannot be cancelled.");
            }
            if (picks.Count != 0)
            {
                throw new InvalidOperationException($"A cancelled answer to {prompt.SiteId} carries {picks.Count} picks.");
            }
            return new ChoiceAnswer(Array.Empty<int>(), true);
        }
        var copy = CopyPicks(prompt, picks, "answer");
        int count = copy.Length;
        if (count > prompt.MaxSelect)
        {
            throw new InvalidOperationException($"The answer to {prompt.SiteId} picks {count} options; at most {prompt.MaxSelect} may be picked.");
        }
        if (!CompletesAutomatically(prompt, count) && !CanConfirm(prompt, count))
        {
            throw new InvalidOperationException($"The answer to {prompt.SiteId} with {count} picks is not a complete selection (bounds {prompt.MinSelect}..{prompt.MaxSelect}, confirm {prompt.RequireConfirm}, skip {prompt.CanSkip}).");
        }
        if (!prompt.Ordered)
        {
            Array.Sort(copy);
        }
        return new ChoiceAnswer(copy, false);
    }

    private static ChoiceAnswer NormalizeSelectorAnswer(ChoicePrompt prompt, ChoiceAnswer answer, IChoiceAnswerer selector)
    {
        if (answer.Cancelled || answer.Picks is null)
        {
            return new ChoiceAnswer(Array.Empty<int>(), false);
        }
        string who = selector is IChoiceSelector named ? $"selector {named.SelectorId}" : "selector";
        return new ChoiceAnswer(CopyPicks(prompt, answer.Picks, who), false);
    }

    private static int[] CopyPicks(ChoicePrompt prompt, IReadOnlyList<int> picks, string who)
    {
        int n = prompt.Options.Count;
        var copy = new int[picks.Count];
        for (int i = 0; i < copy.Length; i++)
        {
            int pick = picks[i];
            if ((uint)pick >= (uint)n)
            {
                throw new InvalidOperationException($"The {who} for {prompt.SiteId} picked option {pick}; the prompt has {n} options.");
            }
            for (int j = 0; j < i; j++)
            {
                if (copy[j] == pick)
                {
                    throw new InvalidOperationException($"The {who} for {prompt.SiteId} picked option {pick} twice.");
                }
            }
            copy[i] = pick;
        }
        return copy;
    }

    public ChoiceRuntime DeepClone()
    {
        var source = _selectors;
        var selectors = new List<IChoiceAnswerer>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            var selector = source[i];
            if (selector is IStatefulChoiceSelector stateful)
            {
                int earlier = IndexOfReference(source, selector, i);
                selectors.Add(earlier >= 0 ? selectors[earlier] : stateful.DeepClone());
            }
            else
            {
                selectors.Add(selector);
            }
        }
        return new ChoiceRuntime(selectors);
    }

    private static int IndexOfReference(List<IChoiceAnswerer> list, IChoiceAnswerer item, int before)
    {
        for (int i = 0; i < before; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }
        return -1;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(ChoiceCanonicalTags.Runtime);
        var selectors = _selectors;
        w.WriteCount(selectors.Count);
        for (int i = 0; i < selectors.Count; i++)
        {
            var selector = selectors[i];
            w.WriteTag(ChoiceCanonicalTags.Selector);
            w.WriteString(((IChoiceSelector)selector).SelectorId);
            var stateful = selector as IStatefulChoiceSelector;
            w.WriteBool(stateful != null);
            stateful?.WriteCanonical(w);
        }
    }
}
