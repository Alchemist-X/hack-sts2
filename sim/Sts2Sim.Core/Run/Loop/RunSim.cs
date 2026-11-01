namespace Sts2Sim.Core.Run;

public sealed class RunSim
{
    private RunSim()
    {
    }

    public static RunSim NewRun(RunConfig config) => throw new NotImplementedException("W2 R6: RunSim.NewRun");

    public static RunSim FromSave(SavedRun save, ImportOptions options) => throw new NotImplementedException("W2 R6: RunSim.FromSave");

    public RunPhase Phase => throw new NotImplementedException("W2 R6: RunSim.Phase");

    public bool IsTerminal => throw new NotImplementedException("W2 R6: RunSim.IsTerminal");

    public RunState State => throw new NotImplementedException("W2 R6: RunSim.State");

    public CombatSim? Combat => throw new NotImplementedException("W2 R6: RunSim.Combat");

    public IReadOnlyList<RunAction> LegalActions() => throw new NotImplementedException("W2 R6: RunSim.LegalActions");

    public void LegalActions(List<RunAction> buffer) => throw new NotImplementedException("W2 R6: RunSim.LegalActions(buffer)");

    public bool IsLegal(RunAction action) => throw new NotImplementedException("W2 R6: RunSim.IsLegal");

    public void Step(RunAction action) => throw new NotImplementedException("W2 R6: RunSim.Step");

    public RunSim Clone() => throw new NotImplementedException("W2 R6: RunSim.Clone");

    public ulong StateHash() => throw new NotImplementedException("W2 R6: RunSim.StateHash");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R6: RunSim.WriteCanonical");
}
