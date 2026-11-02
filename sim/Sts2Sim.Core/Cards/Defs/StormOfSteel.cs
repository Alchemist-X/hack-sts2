namespace Sts2Sim.Core.Cards;

public sealed class StormOfSteel : CardDef
{
    public const string CardId = "STORM_OF_STEEL";
    public const int Cost = 1;

    public StormOfSteel()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var stormOfSteel = play.Card;
        var owner = stormOfSteel.Owner;
        var hand = owner.Hand.Cards.ToList();
        int handSize = hand.Count;
        CardCmd.Discard(cs, hand);
        var shivs = Shiv.CreateInHand(cs, owner, handSize);
        if (stormOfSteel.IsUpgraded)
        {
            foreach (var current in shivs)
            {
                CardCmd.Upgrade(cs, current);
            }
        }
    }
}
