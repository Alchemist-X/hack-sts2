using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Regret : CardDef
{
    public const string CardId = "REGRET";
    public const int Cost = -1;
    public const ushort StateTag = 0xCA32;
    public const ValueProp DamageProps = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Regret()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override bool HasTurnEndInHandEffect => true;

    public override CardExtraState? CreateExtraState() => new RegretState();

    public override AbstractModel? CreateListener(CardInstance card) => new RegretListener(card);

    public static int GetCardsInHand(CardInstance card) => ((RegretState)card.Extra!).CardsInHand;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public override void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
        var state = (RegretState)card.Extra!;
        CreatureCmd.Damage(cs, card.Owner, state.CardsInHand, DamageProps, card, null);
        state.CardsInHand = 0;
    }

    private sealed class RegretState : CardExtraState
    {
        public int CardsInHand { get; set; }

        public override CardExtraState DeepClone() => (RegretState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(CardsInHand);
        }
    }

    private sealed class RegretListener : CardListener
    {
        public RegretListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new RegretListener(newOwner);

        public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
        {
            var owner = Card.Owner;
            if (!participants.Contains(owner))
            {
                return;
            }
            var pile = owner.FindPileContaining(Card);
            if (pile == null || pile.Type != PileType.Hand)
            {
                return;
            }
            ((RegretState)Card.Extra!).CardsInHand = pile.Cards.Count;
        }
    }
}
