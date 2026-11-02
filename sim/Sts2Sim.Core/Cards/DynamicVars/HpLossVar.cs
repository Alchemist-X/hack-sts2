namespace Sts2Sim.Core.Cards;

public class HpLossVar : DynamicVar
{
    public const string DefaultName = "HpLoss";

    public HpLossVar(decimal hpLoss)
        : base(DefaultName, hpLoss)
    {
    }

    public HpLossVar(string name, decimal hpLoss)
        : base(name, hpLoss)
    {
    }
}
