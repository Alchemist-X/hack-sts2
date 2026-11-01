namespace Sts2Sim.Core.Cards;

public sealed class TrashToTreasure : CardDef
{
    public const string CardId = "TRASH_TO_TREASURE";
    public const int Cost = 1;
    public const string TrashToTreasurePowerId = "TRASH_TO_TREASURE_POWER";
    public const decimal PowerAmount = 1m;
    public const int CostUpgrade = -1;

    public TrashToTreasure()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var trashToTreasure = play.Card;
        var owner = trashToTreasure.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(TrashToTreasurePowerId), owner, PowerAmount, owner, trashToTreasure);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
