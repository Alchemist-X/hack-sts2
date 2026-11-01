namespace Sts2Sim.Core.Cards;

public class ExtraDamageVar : DynamicVar
{
    public const string DefaultName = "ExtraDamage";

    public ExtraDamageVar(decimal damage)
        : base(DefaultName, damage)
    {
    }

    public bool IsFromOsty { get; private set; }

    public ExtraDamageVar FromOsty()
    {
        IsFromOsty = true;
        return this;
    }
}
