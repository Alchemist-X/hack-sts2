namespace Sts2Sim.Core.Cards;

public sealed class ToolsOfTheTrade : CardDef
{
    public const string CardId = "TOOLS_OF_THE_TRADE";
    public const int Cost = 1;
    public const int CostUpgrade = -1;
    public const decimal PowerAmount = 1m;

    public ToolsOfTheTrade()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply<ToolsOfTheTradePower>(cs, owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
