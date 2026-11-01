namespace Sts2Sim.Core.Cards;

public class BoolVar : DynamicVar
{
    public BoolVar(string name)
        : base(name, 0m)
    {
    }

    public BoolVar(string name, bool value)
        : base(name, value ? 1m : 0m)
    {
    }

    public bool BoolVal
    {
        get => BaseValue != 0m;
        set => BaseValue = value ? 1m : 0m;
    }
}
