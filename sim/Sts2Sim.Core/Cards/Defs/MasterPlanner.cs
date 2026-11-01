namespace Sts2Sim.Core.Cards;

public sealed class MasterPlanner : CardDef
{
    public const string CardId = "MASTER_PLANNER";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const decimal PowerAmount = 1m;
    public const string MasterPlannerPowerId = "MASTER_PLANNER_POWER";

    public MasterPlanner()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var masterPlanner = play.Card;
        var owner = masterPlanner.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(MasterPlannerPowerId), owner, PowerAmount, owner, masterPlanner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
