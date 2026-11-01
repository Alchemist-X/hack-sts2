namespace Sts2Sim.Core.Cards;

public sealed class Stratagem : CardDef
{
    public const string CardId = "STRATAGEM";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const decimal PowerAmount = 1m;
    public const string StratagemPowerId = "STRATAGEM_POWER";

    public Stratagem()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(StratagemPowerId), owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
