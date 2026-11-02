namespace Sts2Sim.Core.Cards;

public class HealVar : DynamicVar
{
    public const string DefaultName = "Heal";

    public HealVar(decimal healAmount)
        : base(DefaultName, healAmount)
    {
    }

    public HealVar(string name, decimal healAmount)
        : base(name, healAmount)
    {
    }
}
