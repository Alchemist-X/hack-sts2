namespace Sts2Sim.Core.Cards;

public sealed class ChildOfTheStars : CardDef
{
    public const string CardId = "CHILD_OF_THE_STARS";
    public const int Cost = 1;
    public const string ChildOfTheStarsPowerId = "CHILD_OF_THE_STARS_POWER";
    public const string BlockForStarsKey = "BlockForStars";
    public const decimal BaseBlockForStars = 2m;
    public const decimal BlockForStarsUpgrade = 1m;

    public ChildOfTheStars()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(BlockForStarsKey, BaseBlockForStars)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var childOfTheStars = play.Card;
        var owner = childOfTheStars.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(ChildOfTheStarsPowerId), owner, childOfTheStars.Vars[BlockForStarsKey].BaseValue, owner, childOfTheStars);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[BlockForStarsKey].UpgradeValueBy(BlockForStarsUpgrade);
    }
}
