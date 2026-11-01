namespace Sts2Sim.Core.Cards;

public sealed class Scrape : CardDef
{
    public const string CardId = "SCRAPE";
    public const int Cost = 1;
    public const decimal BaseDamage = 7m;
    public const int BaseCards = 4;
    public const decimal DamageUpgrade = 3m;
    public const decimal CardsUpgrade = 1m;

    public Scrape()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var scrape = play.Card;
        DamageCmd.Attack(cs, scrape.Vars.Damage.BaseValue).FromCard(scrape, play).Targeting(target).Execute();
        var drawn = CardPileCmd.DrawCards(cs, scrape.Vars.Cards.IntValue, scrape.Owner);
        var cards = drawn.Where(c => c.EnergyCost.GetWithModifiers(cs, c, CostModifiers.All) != 0 || c.EnergyCost.CostsX).ToList();
        CardCmd.Discard(cs, cards);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
