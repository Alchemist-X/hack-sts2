namespace Sts2Sim.Core.Cards;

public class DamageVar : DynamicVar
{
    public const string DefaultName = "Damage";

    public DamageVar(decimal damage, ValueProp props)
        : base(DefaultName, damage, props)
    {
    }

    public DamageVar(string name, decimal damage, ValueProp props)
        : base(name, damage, props)
    {
    }
}
