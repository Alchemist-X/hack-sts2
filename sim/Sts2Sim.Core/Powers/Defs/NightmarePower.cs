namespace Sts2Sim.Core.Powers;

public sealed class NightmarePower : PowerModel
{
    public const string PowerId = "NIGHTMARE_POWER";

    public const string CardKey = "Card";

    private CardInstance? _selectedCard;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public override PowerStackType StackType => PowerStackType.Counter;

    public CardInstance? SelectedCard => _selectedCard;

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        var card = _selectedCard;
        for (int i = 0; i < Amount; i++)
        {
            if (card == null)
            {
                throw new InvalidOperationException($"{PowerId} has no selected card (Models.Powers/NightmarePower.cs:82-94 dereferences it)");
            }
            var clone = CreateClone(cs, card);
            CardPileCmd.AddGeneratedCardToCombat(cs, clone, PileType.Hand, player);
        }
        PowerCmd.Remove(cs, this);
    }

    public void SetSelectedCard(CombatState cs, CardInstance card)
    {
        var cardModel = CreateClone(cs, card);
        CardCmd.ClearAffliction(cardModel);
        _selectedCard = cardModel;
    }

    private static CardInstance CreateClone(CombatState cs, CardInstance card)
    {
        if (card.GetCombatPile(cs) == null && card.IsInDeck)
        {
            throw new InvalidOperationException("Cannot create a clone of a card that is not in a combat pile.");
        }
        var clone = cs.CloneCard(card);
        clone.ExhaustOnNextPlay = false;
        return clone;
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        var selected = ((NightmarePower)source)._selectedCard;
        _selectedCard = selected?.CloneFor(HasOwner ? Owner as PlayerState : null);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(_selectedCard != null);
        _selectedCard?.WriteCanonical(w);
    }
}
