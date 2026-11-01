namespace Sts2Sim.Core.Cards;

public sealed class RocketPunch : CardDef
{
    public const string CardId = "ROCKET_PUNCH";
    public const int Cost = 2;
    public const decimal BaseDamage = 13m;
    public const int BaseCards = 1;
    public const decimal DamageUpgrade = 1m;
    public const decimal CardsUpgrade = 1m;
    public const int CostReduction = -1;

    public RocketPunch()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new RocketPunchListener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var rocketPunch = play.Card;
        DamageCmd.Attack(cs, rocketPunch.Vars.Damage.BaseValue).FromCard(rocketPunch, play).Targeting(target).Execute();
        CardPileCmd.Draw(cs, rocketPunch.Vars.Cards.BaseValue, rocketPunch.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }

    private sealed class RocketPunchListener : CardListener
    {
        public RocketPunchListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new RocketPunchListener(newOwner);

        public override void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
        {
            var owner = Card.HasOwner ? Card.Owner : null;
            if (!ReferenceEquals(creator, owner))
            {
                return;
            }
            var cardOwner = card.HasOwner ? card.Owner : null;
            if (!ReferenceEquals(cardOwner, owner))
            {
                return;
            }
            if (card.Type != CardType.Status)
            {
                return;
            }
            Card.EnergyCost.AddUntilPlayed(CostReduction);
        }
    }
}
