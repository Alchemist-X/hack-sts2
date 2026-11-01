namespace Sts2Sim.Core.Cards;

public sealed class Pinpoint : CardDef
{
    public const string CardId = "PINPOINT";
    public const int Cost = 3;
    public const decimal BaseDamage = 15m;
    public const decimal DamageUpgrade = 4m;

    public Pinpoint()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var pinpoint = play.Card;
        DamageCmd.Attack(cs, pinpoint.Vars.Damage.BaseValue).FromCard(pinpoint, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    private static void ReduceCostBy(CardInstance card, int amount)
    {
        card.EnergyCost.AddThisTurn(-amount);
    }

    private sealed class Listener : CardListener
    {
        public Listener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new Listener(newOwner);

        public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
        {
            if (!ReferenceEquals(card, Card))
            {
                return;
            }
            if (Card.IsClone)
            {
                return;
            }
            int ownerId = Card.Owner.CombatId;
            int amount = cs.History.CountOf<CardPlayFinishedEntry>(e => e.CardPlay.CardType == CardType.Skill && e.CardPlay.PlayerId == ownerId && e.Stamp.HappenedThisTurn(cs));
            ReduceCostBy(Card, amount);
        }

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
        {
            if (!ReferenceEquals(cardPlay.Card.Owner, Card.Owner))
            {
                return;
            }
            if (cardPlay.Card.Type != CardType.Skill)
            {
                return;
            }
            ReduceCostBy(Card, 1);
        }
    }
}
