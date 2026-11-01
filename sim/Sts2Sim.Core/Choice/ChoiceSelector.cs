namespace Sts2Sim.Core.Choice;

public interface IChoiceSelector : IChoiceAnswerer
{
    string SelectorId { get; }
}

public interface IStatefulChoiceSelector : IChoiceSelector, ICanonical
{
    IStatefulChoiceSelector DeepClone();
}

public sealed class SelectorScope : IDisposable
{
    private readonly ChoiceRuntime _runtime;
    private readonly IChoiceAnswerer _selector;
    private bool _disposed;

    internal SelectorScope(ChoiceRuntime runtime, IChoiceAnswerer selector)
    {
        _runtime = runtime;
        _selector = selector;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _runtime.PopSelector(_selector);
    }
}
