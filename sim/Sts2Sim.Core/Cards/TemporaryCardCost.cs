namespace Sts2Sim.Core.Cards;

public sealed class TemporaryCardCost : ICanonical
{
    private TemporaryCardCost(int cost, bool clearsWhenTurnEnds, bool clearsWhenCardIsPlayed)
    {
        Cost = cost;
        ClearsWhenTurnEnds = clearsWhenTurnEnds;
        ClearsWhenCardIsPlayed = clearsWhenCardIsPlayed;
    }

    public int Cost { get; }

    public bool ClearsWhenTurnEnds { get; }

    public bool ClearsWhenCardIsPlayed { get; }

    public static TemporaryCardCost UntilPlayed(int cost) => new(Math.Max(cost, 0), false, true);

    public static TemporaryCardCost ThisTurn(int cost) => new(Math.Max(cost, 0), true, true);

    public static TemporaryCardCost ThisCombat(int cost) => new(Math.Max(cost, 0), false, false);

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CardCanonicalTags.TemporaryCardCost);
        w.WriteInt32(Cost);
        w.WriteBool(ClearsWhenTurnEnds);
        w.WriteBool(ClearsWhenCardIsPlayed);
    }
}
