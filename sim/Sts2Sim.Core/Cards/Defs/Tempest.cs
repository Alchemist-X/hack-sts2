namespace Sts2Sim.Core.Cards;

public sealed class Tempest : CardDef
{
    public const string CardId = "TEMPEST";
    public const int Cost = 0;
    public const int UpgradeExtraOrbs = 1;

    public Tempest()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self, costsX: true)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var tempest = play.Card;
        int numOfOrbs = tempest.ResolveEnergyXValue(cs);
        if (tempest.IsUpgraded)
        {
            numOfOrbs += UpgradeExtraOrbs;
        }
        for (int i = 0; i < numOfOrbs; i++)
        {
            OrbCmd.Channel<LightningOrb>(cs, tempest.Owner);
        }
    }
}
