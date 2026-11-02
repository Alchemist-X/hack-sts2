namespace Sts2Sim.Core.Cards;

public class StarsVar : DynamicVar
{
    public const string DefaultName = "Stars";

    public StarsVar(int stars)
        : this(DefaultName, stars)
    {
    }

    public StarsVar(string name, int stars)
        : base(name, stars)
    {
    }
}
