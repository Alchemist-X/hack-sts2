namespace Sts2Sim.Core.Cards;

public sealed class Automation : CardDef
{
    public const string CardId = "AUTOMATION";
    public const int Cost = 1;
    public const string AutomationPowerId = "AUTOMATION_POWER";
    public const int BaseEnergy = 1;
    public const int CostUpgrade = -1;

    public Automation()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var automation = play.Card;
        var owner = automation.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(AutomationPowerId), owner, automation.Vars.Energy.IntValue, owner, automation);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
