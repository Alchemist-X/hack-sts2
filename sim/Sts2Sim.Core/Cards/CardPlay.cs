namespace Sts2Sim.Core.Cards;

public sealed class CardPlay
{
    public CardPlay(CardInstance card, CreatureState? target)
    {
        Card = card;
        Target = target;
    }

    public CardInstance Card { get; }

    public CreatureState? Target { get; set; }

    public PileType ResultPile { get; set; }

    public int EnergySpent { get; set; }

    public int StarsSpent { get; set; }

    public bool IsAutoPlay { get; set; }

    public int PlayIndex { get; set; }

    public int PlayCount { get; set; } = 1;

    public bool IsFirstInSeries => PlayIndex == 0;

    public bool IsLastInSeries => PlayIndex == PlayCount - 1;
}
