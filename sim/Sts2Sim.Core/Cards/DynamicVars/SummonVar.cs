namespace Sts2Sim.Core.Cards;

public class SummonVar : DynamicVar
{
    public const string DefaultName = "Summon";

    public SummonVar(decimal summonAmount)
        : base(DefaultName, summonAmount)
    {
    }

    public SummonVar(string name, decimal summonAmount)
        : base(name, summonAmount)
    {
    }
}
