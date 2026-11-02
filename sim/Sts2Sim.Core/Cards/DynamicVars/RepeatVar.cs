namespace Sts2Sim.Core.Cards;

public class RepeatVar : DynamicVar
{
    public const string DefaultName = "Repeat";

    public RepeatVar(int times)
        : base(DefaultName, times)
    {
    }

    public RepeatVar(string name, int times)
        : base(name, times)
    {
    }
}
