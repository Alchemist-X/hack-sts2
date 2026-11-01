namespace Sts2Sim.Core.Cards;

public sealed class WellLaidPlans : CardDef
{
    public const string CardId = "WELL_LAID_PLANS";
    public const int Cost = 2;
    public const decimal PowerAmount = 1m;
    public const int CostUpgrade = -1;
    public const string WellLaidPlansPowerId = "WELL_LAID_PLANS_POWER";

    public WellLaidPlans()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, WellLaidPlansPowerId, owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
