namespace Sts2Sim.Core.Cards;

public sealed class BulletTime : CardDef
{
    public const string CardId = "BULLET_TIME";
    public const int Cost = 3;
    public const string NoDrawPowerId = "NO_DRAW_POWER";
    public const decimal NoDrawAmount = 1m;
    public const int CostUpgrade = -1;

    public BulletTime()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bulletTime = play.Card;
        var owner = bulletTime.Owner;
        var hand = owner.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            var current = hand[i];
            if (!current.EnergyCost.CostsX)
            {
                current.SetToFreeThisTurn();
            }
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(NoDrawPowerId), owner, NoDrawAmount, owner, bulletTime);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
