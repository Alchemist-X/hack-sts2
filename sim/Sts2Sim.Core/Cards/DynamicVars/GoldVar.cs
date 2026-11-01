namespace Sts2Sim.Core.Cards;

public class GoldVar : DynamicVar
{
    public const string DefaultName = "Gold";

    public GoldVar(int gold)
        : base(DefaultName, gold)
    {
    }

    public GoldVar(string name, int gold)
        : base(name, gold)
    {
    }
}
