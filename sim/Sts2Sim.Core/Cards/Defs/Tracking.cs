namespace Sts2Sim.Core.Cards;

public sealed class Tracking : CardDef
{
    public const string CardId = "TRACKING";
    public const int Cost = 2;
    public const decimal PowerAmount = 50m;
    public const int CostUpgrade = -1;
    public const string TrackingPowerId = "TRACKING_POWER";

    public Tracking()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, TrackingPowerId, owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
