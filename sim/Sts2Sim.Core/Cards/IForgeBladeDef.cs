namespace Sts2Sim.Core.Cards;

public interface IForgeBladeDef
{
    bool GetCreatedThroughForge(CardInstance card);

    void SetCreatedThroughForge(CardInstance card, bool value);

    void AddDamage(CardInstance card, decimal amount);

    void SetRepeats(CardInstance card, decimal amount);
}
