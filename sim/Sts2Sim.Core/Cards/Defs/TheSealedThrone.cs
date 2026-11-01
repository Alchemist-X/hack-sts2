namespace Sts2Sim.Core.Cards;

public sealed class TheSealedThrone : CardDef
{
    public const string CardId = "THE_SEALED_THRONE";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const int StarCost = 3;
    public const decimal SealedThroneAmount = 1m;
    public const string TheSealedThronePowerId = "THE_SEALED_THRONE_POWER";

    public TheSealedThrone()
        : base(CardId, Cost, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var theSealedThrone = play.Card;
        var owner = theSealedThrone.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(TheSealedThronePowerId), owner, SealedThroneAmount, owner, theSealedThrone);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
