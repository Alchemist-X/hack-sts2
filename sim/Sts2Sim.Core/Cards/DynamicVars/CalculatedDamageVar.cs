namespace Sts2Sim.Core.Cards;

public class CalculatedDamageVar : CalculatedVar
{
    public const string DefaultName = "CalculatedDamage";

    public CalculatedDamageVar(ValueProp props)
        : base(DefaultName, props)
    {
    }

    public bool IsFromOsty { get; private set; }

    public CalculatedDamageVar FromOsty()
    {
        IsFromOsty = true;
        return this;
    }

    protected override DynamicVar GetExtraVar(DynamicVarSet vars) => vars.ExtraDamage;
}
