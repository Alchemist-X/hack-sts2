using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Normality : CardDef
{
    public const string CardId = "NORMALITY";
    public const int Cost = -1;
    public const int NumOfCardsPerTurn = 3;
    public const string CalculatedCardsKey = "CalculatedCards";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Unplayable });

    public static readonly Func<CombatState, CardInstance, CreatureState?, decimal> CardsPlayedCapped = static (cs, card, _) => Math.Min(NumOfCardsPerTurn, CardsPlayedThisTurn(cs, card));

    public Normality()
        : base(CardId, Cost, CardType.Curse, CardRarity.Curse, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CalculationBaseVar(NumOfCardsPerTurn),
        new CalculationExtraVar(-1m),
        new CalculatedVar(CalculatedCardsKey).WithMultiplier(CardsPlayedCapped)
    };

    public override AbstractModel? CreateListener(CardInstance card) => new Listener(card);

    public override void OnPlay(CombatState cs, CardPlay play)
    {
    }

    public static int CardsPlayedThisTurn(CombatState cs, CardInstance card)
    {
        int ownerId = card.Owner.CombatId;
        return cs.History.CountOf<CardPlayStartedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay.PlayerId == ownerId);
    }

    private static bool ShouldPreventCardPlay(CombatState cs, CardInstance card) => CardsPlayedThisTurn(cs, card) >= NumOfCardsPerTurn;

    private sealed class Listener : CardListener
    {
        public Listener(CardInstance card)
            : base(card)
        {
        }

        public override AbstractModel CloneFor(CardInstance newOwner) => new Listener(newOwner);

        public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
        {
            if (!ReferenceEquals(card.Owner, Card.Owner))
            {
                return true;
            }
            if (Card.GetPileType(cs) != PileType.Hand)
            {
                return true;
            }
            return !ShouldPreventCardPlay(cs, Card);
        }
    }
}
