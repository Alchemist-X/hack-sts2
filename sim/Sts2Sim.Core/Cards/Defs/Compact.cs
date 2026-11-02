namespace Sts2Sim.Core.Cards;

public sealed class Compact : CardDef
{
    public const string CardId = "COMPACT";
    public const int Cost = 1;
    public const decimal BaseBlock = 6m;
    public const decimal BlockUpgrade = 1m;
    public const string FuelId = "FUEL";

    public Compact()
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
        var compact = play.Card;
        var owner = compact.Owner;
        CreatureCmd.GainBlock(cs, owner, compact.Vars.Block, play);
        var hand = owner.Hand.Cards;
        var statuses = new List<CardInstance>(hand.Count);
        for (int i = 0; i < hand.Count; i++)
        {
            var c = hand[i];
            if (c != null && c.IsTransformable(cs) && c.Type == CardType.Status)
            {
                statuses.Add(c);
            }
        }
        var transformations = new List<CardTransformation>(statuses.Count);
        for (int i = 0; i < statuses.Count; i++)
        {
            var current = statuses[i];
            var cardModel = cs.CreateCard(FuelId, owner);
            if (compact.IsUpgraded)
            {
                CardCmd.Upgrade(cs, cardModel);
            }
            transformations.Add(new CardTransformation(cs, current, cardModel));
        }
        CardCmd.Transform(cs, transformations, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
