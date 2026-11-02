namespace Sts2Sim.Core.Powers;

public sealed class PaleBlueDotPower : PowerModel
{
    public const string PowerId = "PALE_BLUE_DOT_POWER";

    public const string CardPlayThresholdKey = "CardPlay";

    public const int CardPlayThresholdValue = 5;

    public const string DrawCardsNextTurnPowerId = "DRAW_CARDS_NEXT_TURN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public bool AlreadyActivatedThisTurn { get; private set; }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner as PlayerState))
        {
            return;
        }
        if (AlreadyActivatedThisTurn)
        {
            return;
        }
        if (CardPlaysFinishedThisTurn(cs) < CardPlayThresholdValue)
        {
            return;
        }
        AlreadyActivatedThisTurn = true;
        PowerCmd.Apply(cs, PowerRegistry.Create(DrawCardsNextTurnPowerId), Owner, Amount, Owner, null);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        AlreadyActivatedThisTurn = false;
    }

    private int CardPlaysFinishedThisTurn(CombatState cs)
    {
        var owner = Owner;
        return cs.History.CountOf<CardPlayFinishedEntry>(c => c.Stamp.HappenedThisTurn(cs) && owner is PlayerState p && c.CardPlay.PlayerId == p.CombatId);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(AlreadyActivatedThisTurn);
    }
}
