namespace Sts2Sim.Core.Cards;

public abstract class CardListener : AbstractModel, IOwnedListener<CardInstance>
{
    protected CardListener(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
    }

    public CardInstance Card { get; }

    public abstract AbstractModel CloneFor(CardInstance newOwner);
}
