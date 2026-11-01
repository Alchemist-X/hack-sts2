namespace Sts2Sim.Core.Run;

public abstract partial class AncientEventModel : EventModel
{
    protected void Done() => throw new NotImplementedException("W2 R5: AncientEventModel.Done");

    protected void ApplyArrivalHeal(RunState rs) => throw new NotImplementedException("W2 R5: AncientEventModel.ArrivalHeal");
}
