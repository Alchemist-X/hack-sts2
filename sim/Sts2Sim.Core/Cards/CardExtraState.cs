namespace Sts2Sim.Core.Cards;

public abstract class CardExtraState : ICanonical
{
    public abstract CardExtraState DeepClone();

    public abstract void WriteCanonical(CanonicalWriter w);
}
