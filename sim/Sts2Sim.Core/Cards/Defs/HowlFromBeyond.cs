namespace Sts2Sim.Core.Cards;

public sealed class HowlFromBeyond : CardDef
{
    public const string CardId = "HOWL_FROM_BEYOND";
    public const int Cost = 3;
    public const decimal BaseDamage = 18m;
    public const decimal DamageUpgrade = 6m;

    public HowlFromBeyond()
        : base(CardId, Cost, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var howlFromBeyond = play.Card;
        DamageCmd.Attack(cs, howlFromBeyond.Vars.Damage.BaseValue).FromCard(howlFromBeyond, play).TargetingAllOpponents().Execute();
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

        public override void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player)
        {
            var howlFromBeyond = Card;
            var pile = howlFromBeyond.GetCombatPile(cs);
            if (pile != null && pile.Type == PileType.Exhaust && ReferenceEquals(player, howlFromBeyond.Owner))
            {
                CardCmd.AutoPlay(cs, howlFromBeyond, null);
            }
        }
    }
}
