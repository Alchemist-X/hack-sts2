namespace Sts2Sim.Core.Cards;

public sealed class Calamity : CardDef
{
    public const string CardId = "CALAMITY";
    public const int Cost = 3;
    public const string PowerId = "CALAMITY_POWER";
    public const decimal PowerAmount = 1m;
    public const int CostUpgrade = -1;

    public Calamity()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var creature = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PowerId), creature, PowerAmount, creature, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
