namespace Sts2Sim.Core.Cards;

public sealed class SecondWind : CardDef
{
    public const string CardId = "SECOND_WIND";
    public const int Cost = 1;
    public const decimal BaseBlock = 5m;
    public const decimal BlockUpgrade = 2m;

    public SecondWind()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var cards = GetCards(owner);
        for (int i = 0; i < cards.Count; i++)
        {
            CardCmd.Exhaust(cs, cards[i]);
            CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }

    private static List<CardInstance> GetCards(PlayerState owner)
    {
        var hand = owner.Hand.Cards;
        var result = new List<CardInstance>(hand.Count);
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Type != CardType.Attack)
            {
                result.Add(hand[i]);
            }
        }
        return result;
    }
}
