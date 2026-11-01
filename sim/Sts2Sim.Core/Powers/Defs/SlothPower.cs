namespace Sts2Sim.Core.Powers;

public sealed class SlothPower : PowerModel
{
    public const string PowerId = "SLOTH_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int CardsPlayedThisTurn { get; private set; }

    public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return true;
        }
        return CardsPlayedThisTurn < Amount;
    }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner))
        {
            return;
        }
        CardsPlayedThisTurn++;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        CardsPlayedThisTurn = 0;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(CardsPlayedThisTurn);
    }
}
