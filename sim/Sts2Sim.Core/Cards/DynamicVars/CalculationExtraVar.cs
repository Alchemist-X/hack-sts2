namespace Sts2Sim.Core.Cards;

public class CalculationExtraVar : DynamicVar
{
    public const string DefaultName = "CalculationExtra";

    public CalculationExtraVar(decimal baseValue)
        : base(DefaultName, baseValue)
    {
    }
}
