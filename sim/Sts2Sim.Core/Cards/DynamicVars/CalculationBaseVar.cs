namespace Sts2Sim.Core.Cards;

public class CalculationBaseVar : DynamicVar
{
    public const string DefaultName = "CalculationBase";

    public CalculationBaseVar(decimal baseValue)
        : base(DefaultName, baseValue)
    {
    }
}
