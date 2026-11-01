namespace Sts2Sim.Core.Cards;

public sealed class ThrummingHatchet : CardDef
{
    public const string CardId = "THRUMMING_HATCHET";
    public const int Cost = 1;
    public const decimal BaseDamage = 11m;
    public const decimal DamageUpgrade = 3m;

    public ThrummingHatchet()
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
        var thrummingHatchet = play.Card;
        DamageCmd.Attack(cs, thrummingHatchet.Vars.Damage.BaseValue).FromCard(thrummingHatchet, play).Targeting(target).Execute();
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

        public override void BeforeHandDraw(CombatState cs, PlayerState player)
        {
            Bolas.ReturnToHandIfPlayedLastTurn(cs, Card, player);
        }
    }
}
