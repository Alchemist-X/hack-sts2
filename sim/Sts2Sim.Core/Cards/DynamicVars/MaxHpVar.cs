namespace Sts2Sim.Core.Cards;

public class MaxHpVar : DynamicVar
{
    public const string DefaultName = "MaxHp";

    public MaxHpVar(decimal maxHp)
        : base(DefaultName, maxHp)
    {
    }

    public MaxHpVar(string name, decimal maxHp)
        : base(name, maxHp)
    {
    }
}
