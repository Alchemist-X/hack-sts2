namespace Sts2Sim.Core.Relics;

public sealed class BookOfFiveRings : RelicModel
{
    public const string RelicId = "BOOK_OF_FIVE_RINGS";

    private int _cardsAdded;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool ShowCounter => true;

    public override int DisplayAmount => CardsAddedSinceLastTrigger;

    public int CardsAdded
    {
        get => _cardsAdded;
        set => _cardsAdded = value;
    }

    private int CardsAddedSinceLastTrigger => CardsAdded % DynamicVars.Cards.IntValue;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(5),
        new HealVar(20m)
    };

    public override bool IsAllowed(RunState rs) => RelicCatalog.IsBeforeAct3TreasureChest(rs);

    public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (Owner.IsDead || !card.HasOwner || !ReferenceEquals(card.Owner, Owner) || card.GetPileType(rs.Combat) != PileType.Deck)
        {
            return;
        }
        CardsAdded++;
        if (CardsAddedSinceLastTrigger == 0)
        {
            CreatureCmd.Heal(rs, Owner, DynamicVars.Heal.BaseValue);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_cardsAdded);
    }
}
