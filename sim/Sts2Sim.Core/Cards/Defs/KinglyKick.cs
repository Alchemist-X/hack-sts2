namespace Sts2Sim.Core.Cards;

public sealed class KinglyKick : CardDef
{
    public const string CardId = "KINGLY_KICK";
    public const int Cost = 4;
    public const decimal BaseDamage = 27m;
    public const decimal DamageUpgrade = 8m;
    public const int DrawnCostChange = -1;

    public KinglyKick()
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
        var kinglyKick = play.Card;
        DamageCmd.Attack(cs, kinglyKick.Vars.Damage.BaseValue).FromCard(kinglyKick, play).Targeting(target).Execute();
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

        public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
        {
            if (!ReferenceEquals(card, Card))
            {
                return;
            }
            Card.EnergyCost.AddThisCombat(DrawnCostChange);
        }
    }
}
