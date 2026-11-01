namespace Sts2Sim.Core.Run;

public sealed partial class CardCreationResult
{
    public CardCreationResult(CardInstance card)
    {
        Card = card;
    }

    public CardInstance Card { get; private set; }

    public CardInstance? OriginalCard { get; private set; }

    public bool HasBeenModified => OriginalCard != null;

    public void ModifyCard(CardInstance card) => throw new NotImplementedException("W2 R3: CardCreationResult.ModifyCard");
}
