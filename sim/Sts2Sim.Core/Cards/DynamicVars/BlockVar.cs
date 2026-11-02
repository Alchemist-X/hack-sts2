namespace Sts2Sim.Core.Cards;

public class BlockVar : DynamicVar
{
    public const string DefaultName = "Block";

    public BlockVar(decimal block, ValueProp props)
        : base(DefaultName, block, props)
    {
    }

    public BlockVar(string name, decimal block, ValueProp props)
        : base(name, block, props)
    {
    }
}
