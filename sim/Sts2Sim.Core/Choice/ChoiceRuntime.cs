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

    public void Dispose() => _runtime?.PopContext(_depth);
}

public readonly record struct ChoiceContextFrame(ChoiceContextKind Kind, Action? OnFirstPause);

public sealed class ChoiceRuntime : ICanonical
{
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

    public IReadOnlyList<ChoiceContextFrame> Contexts => (IReadOnlyList<ChoiceContextFrame>?)_contexts ?? Array.Empty<ChoiceContextFrame>();

    public int ContextDepth => _contexts?.Count ?? 0;

    public ContextScope PushContext(ChoiceContextKind kind, Action? onFirstPause = null)
    {
        var contexts = _contexts ??= new List<ChoiceContextFrame>(4);
        int depth = contexts.Count;
        contexts.Add(new ChoiceContextFrame(kind, onFirstPause));
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

    public void PushSelector(IChoiceAnswerer selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _selectors.Add(selector);
    }

    public void PopSelector()
    {
        if (_selectors.Count == 0)
        {
            throw new InvalidOperationException("No card selector to pop.");
        }
        _selectors.RemoveAt(_selectors.Count - 1);
    }

    public ChoiceAnswer Ask(ChoicePrompt prompt)
        => throw new NotImplementedException("ChoiceRuntime.Ask: choice agent (CONTRACTS Part B §B1)");

    public ChoiceRuntime DeepClone() => new(new List<IChoiceAnswerer>(_selectors));

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(ChoiceCanonicalTags.Runtime);
        var selectors = _selectors;
        w.WriteCount(selectors.Count);
        for (int i = 0; i < selectors.Count; i++)
        {
            var selector = selectors[i];
            w.WriteTag(ChoiceCanonicalTags.Selector);
            w.WriteString(selector.GetType().FullName);
            var canonical = selector as ICanonical;
            w.WriteBool(canonical != null);
            canonical?.WriteCanonical(w);
        }
    }
}
