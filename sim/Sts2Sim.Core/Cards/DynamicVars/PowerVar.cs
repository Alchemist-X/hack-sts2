namespace Sts2Sim.Core.Cards;

public class PowerVar<T> : DynamicVar where T : PowerModel
{
    public PowerVar(decimal powerAmount)
        : base(typeof(T).Name, powerAmount)
    {
    }

    public PowerVar(string name, decimal powerAmount)
        : base(name, powerAmount)
    {
    }
}
