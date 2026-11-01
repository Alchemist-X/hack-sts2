namespace Sts2Sim.Core.Cards;

public enum UpgradeDisplay
{
    Normal,
    Upgraded,
    UpgradePreview
}

public class IfUpgradedVar : DynamicVar
{
    public const string DefaultName = "IfUpgraded";

    public IfUpgradedVar(UpgradeDisplay upgradeDisplay)
        : base(DefaultName, (int)upgradeDisplay)
    {
        Display = upgradeDisplay;
    }

    public IfUpgradedVar(string name, decimal amount)
        : base(name, amount)
    {
    }

    public UpgradeDisplay Display { get; }
}
