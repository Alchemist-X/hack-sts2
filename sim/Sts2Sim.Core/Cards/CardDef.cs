namespace Sts2Sim.Core.Cards;

public readonly record struct CardLocation(PileType PileType, CardPilePosition Position);

public abstract class CardDef
{
    protected CardDef(string id, int canonicalCost, CardType type, CardRarity rarity, TargetType targetType, bool costsX = false)
    {
        Id = id;
        CanonicalCost = canonicalCost;
        Type = type;
        Rarity = rarity;
        TargetType = targetType;
        CostsX = costsX;
    }

    public string Id { get; }

    public int CanonicalCost { get; }

    public bool CostsX { get; }

    public CardType Type { get; }

    public CardRarity Rarity { get; }

    public TargetType TargetType { get; }

    public virtual int CanonicalStarCost => -1;

    public virtual bool HasStarCostX => false;

    public virtual int MaxUpgradeLevel => 1;

    public virtual bool CanBeGeneratedInCombat => true;

    public virtual bool CanBeGeneratedByModifiers => true;

    public virtual IReadOnlyCollection<CardKeyword> CanonicalKeywords => Array.Empty<CardKeyword>();

    public virtual IReadOnlyCollection<CardTag> CanonicalTags => Array.Empty<CardTag>();

    public virtual bool GainsBlock => false;

    public virtual bool IsBasicStrikeOrDefend
    {
        get
        {
            if (Rarity != CardRarity.Basic)
            {
                return false;
            }
            var tags = CanonicalTags;
            if (tags.Contains(CardTag.Strike))
            {
                return true;
            }
            if (tags.Contains(CardTag.Defend))
            {
                return true;
            }
            return false;
        }
    }

    public virtual bool HasTurnEndInHandEffect => false;

    protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

    public DynamicVarSet CreateVars() => new(CanonicalVars);

    public abstract void OnPlay(CombatState cs, CardPlay play);

    public virtual void OnUpgrade(CardInstance card)
    {
    }

    public virtual bool IsPlayable(CombatState cs, CardInstance card) => true;

    public virtual void OnTurnEndInHand(CombatState cs, CardInstance card)
    {
    }

    public virtual CardLocation GetResultLocationForCardPlay(CombatState cs, CardInstance card)
    {
        if (card.IsDupe || Type == CardType.Power)
        {
            return new CardLocation(PileType.None, CardPilePosition.Bottom);
        }
        if (card.ExhaustOnNextPlay || card.HasKeyword(CardKeyword.Exhaust))
        {
            card.ExhaustOnNextPlay = false;
            return new CardLocation(PileType.Exhaust, CardPilePosition.Bottom);
        }
        return new CardLocation(PileType.Discard, CardPilePosition.Bottom);
    }
}
