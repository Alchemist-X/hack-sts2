namespace Sts2Sim.Core.Cards;

public sealed class BansheesCry : CardDef
{
    public const string CardId = "BANSHEES_CRY";
    public const int Cost = 9;
    public const decimal BaseDamage = 33m;
    public const int BaseEnergy = 2;
    public const int CostUpgrade = -2;

    public BansheesCry()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new EnergyVar(BaseEnergy)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bansheesCry = play.Card;
        DamageCmd.Attack(cs, bansheesCry.Vars.Damage.BaseValue).FromCard(bansheesCry, play).TargetingAllOpponents().Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
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
            int num = cs.History.CountOf<CardPlayFinishedEntry>(e => e.WasEthereal && e.CardPlay.PlayerId == ownerId);
            Card.EnergyCost.AddThisCombat(-num * Card.Vars.Energy.IntValue);
        }

        public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
        {
            if (!ReferenceEquals(cardPlay.Card.Owner, Card.Owner))
            {
                return;
            }
            if (!cardPlay.Card.HasKeyword(CardKeyword.Ethereal))
            {
                return;
            }
            Card.EnergyCost.AddThisCombat(-Card.Vars.Energy.IntValue);
        }
    }
}
