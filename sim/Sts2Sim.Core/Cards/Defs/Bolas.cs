namespace Sts2Sim.Core.Cards;

public sealed class Bolas : CardDef
{
    public const string CardId = "BOLAS";
    public const int Cost = 0;
    public const decimal BaseDamage = 3m;
    public const decimal DamageUpgrade = 1m;

    public Bolas()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
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
        var bolas = play.Card;
        DamageCmd.Attack(cs, bolas.Vars.Damage.BaseValue).FromCard(bolas, play).Targeting(target).Execute();
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }

    internal static void ReturnToHandIfPlayedLastTurn(CombatState cs, CardInstance card, PlayerState player)
    {
        var owner = card.Owner;
        if (!ReferenceEquals(player, owner))
        {
            return;
        }
        if (!cs.History.AnyOf<CardPlayFinishedEntry>(e => e.Stamp.HappenedLastPlayerTurn(owner) && e.CardPlay.IsPlayOf(card)))
        {
            return;
        }
        if (card.GetPileType(cs) != PileType.Hand)
        {
            CardPileCmd.Add(cs, card, PileType.Hand);
        }
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
            ReturnToHandIfPlayedLastTurn(cs, Card, player);
        }
    }
}
