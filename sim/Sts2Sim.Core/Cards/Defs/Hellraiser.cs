namespace Sts2Sim.Core.Cards;

public sealed class Hellraiser : CardDef
{
    public const string CardId = "HELLRAISER";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const decimal PowerAmount = 1m;

    public Hellraiser()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var hellraiser = play.Card;
        var owner = hellraiser.Owner;
        PowerCmd.Apply<HellraiserPower>(cs, owner, PowerAmount, owner, hellraiser);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
