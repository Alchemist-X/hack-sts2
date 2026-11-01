namespace Sts2Sim.Core.Cards;

public sealed class MakeItSo : CardDef
{
    public const string CardId = "MAKE_IT_SO";
    public const int Cost = 0;
    public const decimal BaseDamage = 6m;
    public const int BaseCards = 3;
    public const decimal DamageUpgrade = 3m;

    public MakeItSo()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var target = play.Target;
        ArgumentNullException.ThrowIfNull(target);
        var makeItSo = play.Card;
        DamageCmd.Attack(cs, makeItSo.Vars.Damage.BaseValue).FromCard(makeItSo, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    private sealed class Listener : CardListener
    {
        public Listener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new Listener(newOwner);

        public override void AfterCardPlayedLate(CombatState cs, CardPlay cardPlay)
        {
            var makeItSo = Card;
            if (!ReferenceEquals(cardPlay.Card.Owner, makeItSo.Owner) || cardPlay.Card.Type != CardType.Skill || makeItSo.GetPileType(cs) == PileType.Hand)
            {
                return;
            }
            int ownerId = makeItSo.Owner.CombatId;
            int num = cs.History.CountOf<CardPlayFinishedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay.CardType == CardType.Skill && e.CardPlay.PlayerId == ownerId);
            if (num % makeItSo.Vars.Cards.IntValue == 0)
            {
                CardPileCmd.Add(cs, makeItSo, PileType.Hand);
            }
        }
    }
}
