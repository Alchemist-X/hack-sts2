namespace Sts2Sim.Core.Cards;

public class CardsVar : DynamicVar
{
    public const string DefaultName = "Cards";

    public CardsVar(int cards)
        : base(DefaultName, cards)
    {
    }

    public CardsVar(string name, int cards)
        : base(name, cards)
    {
    }
}
