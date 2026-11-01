namespace Sts2Sim.Core.Cards;

public class EnergyVar : DynamicVar
{
    public const string DefaultName = "Energy";

    public EnergyVar(int energy)
        : this(DefaultName, energy)
    {
    }

    public EnergyVar(string name, int energy)
        : base(name, energy)
    {
    }
}
