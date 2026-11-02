namespace Sts2Sim.Core.Cards;

public sealed class IAmInvincible : CardDef
{
    public const string CardId = "I_AM_INVINCIBLE";
    public const int Cost = 1;
    public const decimal BaseBlock = 10m;
    public const decimal BlockUpgrade = 3m;
    public const int AutoPlayCount = 1;

    public IAmInvincible()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var amInvincible = play.Card;
        CreatureCmd.GainBlock(cs, amInvincible.Owner, amInvincible.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
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
            var amInvincible = Card;
            if (!ReferenceEquals(player, amInvincible.Owner))
            {
                return;
            }
            var cards = amInvincible.Owner.DrawPile.Cards;
            if (cards.Count > 0 && ReferenceEquals(cards[0], amInvincible))
            {
                CardPileCmd.AutoPlayFromDrawPile(cs, amInvincible.Owner, AutoPlayCount, CardPilePosition.Top, forceExhaust: false);
            }
        }
    }
}
