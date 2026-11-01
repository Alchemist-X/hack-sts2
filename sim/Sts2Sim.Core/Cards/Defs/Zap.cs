namespace Sts2Sim.Core.Cards;

public sealed class Zap : CardDef
{
    public const string CardId = "ZAP";
    public const int Cost = 1;
    public const int CostUpgrade = -1;

    public Zap()
        : base(CardId, Cost, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        OrbCmd.Channel<LightningOrb>(cs, play.Card.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
