using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Guilty : CardDef
{
    public const string CardId = "GUILTY";
    public const int Cost = -1;
    public const int MaxCombats = 5;
    public const string CombatsKey = "Combats";
    public const ushort StateTag = 0xCB62;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public Guilty()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(CombatsKey, MaxCombats)
    };

    public override CardExtraState? CreateExtraState() => new GuiltyState();

    public override AbstractModel? CreateListener(CardInstance card) => new GuiltyListener(card);

    public static int GetCombatsSeen(CardInstance card) => ((GuiltyState)card.Extra!).CombatsSeen;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    private static void SetCombatsSeen(CardInstance card, int value)
    {
        ((GuiltyState)card.Extra!).CombatsSeen = value;
        card.Vars[CombatsKey].BaseValue = MaxCombats - value;
    }

    private sealed class GuiltyState : CardExtraState
    {
        public int CombatsSeen { get; set; }

        public override CardExtraState DeepClone() => (GuiltyState)MemberwiseClone();

        public override void WriteCanonical(CanonicalWriter w)
        {
            w.WriteTag(StateTag);
            w.WriteInt32(CombatsSeen);
        }
    }

    private sealed class GuiltyListener : CardListener
    {
        public GuiltyListener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new GuiltyListener(newOwner);

        public override void AfterCombatEnd(RunState rs, CombatRoom? room)
        {
            if (!Card.IsInDeck)
            {
                return;
            }
            SetCombatsSeen(Card, GetCombatsSeen(Card) + 1);
            if (GetCombatsSeen(Card) >= MaxCombats && Card.IsInDeck)
            {
                CardPileCmd.RemoveFromDeck(rs, Card);
            }
        }
    }
}
