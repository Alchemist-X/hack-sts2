namespace Sts2Sim.Core.Cards;

public sealed class Overclock : CardDef
{
    public const string CardId = "OVERCLOCK";
    public const int Cost = 0;
    public const string BurnCardId = "BURN";
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;

    public Overclock()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
        var burn = cs.CreateCard(BurnCardId, owner);
        CardPileCmd.AddGeneratedCardToCombat(cs, burn, PileType.Discard, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
