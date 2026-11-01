namespace Sts2Sim.Core.Cards;

public sealed class DarkEmbrace : CardDef
{
    public const string CardId = "DARK_EMBRACE";
    public const int Cost = 2;
    public const decimal DarkEmbraceAmount = 1m;
    public const int CostUpgrade = -1;

    public DarkEmbrace()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var darkEmbrace = play.Card;
        var owner = darkEmbrace.Owner;
        PowerCmd.Apply<DarkEmbracePower>(cs, owner, DarkEmbraceAmount, owner, darkEmbrace);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
