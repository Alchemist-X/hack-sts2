namespace Sts2Sim.Core.Cards;

public class OstyDamageVar : DynamicVar
{
    public const string DefaultName = "OstyDamage";

    public OstyDamageVar(decimal damage, ValueProp props)
        : base(DefaultName, damage, props)
    {
    }

    public OstyDamageVar(string name, decimal damage, ValueProp props)
        : base(name, damage, props)
    {
    }
}
