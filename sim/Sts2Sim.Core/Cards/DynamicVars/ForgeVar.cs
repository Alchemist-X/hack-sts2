namespace Sts2Sim.Core.Cards;

public class ForgeVar : DynamicVar
{
    public const string DefaultName = "Forge";

    public ForgeVar(int forge)
        : this(DefaultName, forge)
    {
    }

    public ForgeVar(string name, int forge)
        : base(name, forge)
    {
    }
}
