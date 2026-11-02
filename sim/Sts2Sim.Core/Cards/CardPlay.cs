namespace Sts2Sim.Core.Cards;

public sealed class CardPlay
{
    public CardPlay(CombatState cs, CardInstance card, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        PlayId = cs.AllocateCardPlayId();
        Card = card;
        Target = target;
    }

    public int PlayId { get; }

    public CardInstance Card { get; }

    public CreatureState? Target { get; set; }

    public PileType ResultPile { get; set; }

    public ResourceInfo Resources { get; set; }

    public bool IsAutoPlay { get; set; }

    public int PlayIndex { get; set; }

    public int PlayCount { get; set; } = 1;

    public bool IsFirstInSeries => PlayIndex == 0;

    public bool IsLastInSeries => PlayIndex == PlayCount - 1;
}
