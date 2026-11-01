namespace Sts2Sim.Core.Powers;

public sealed class TenderPower : PowerModel
{
    public const string PowerId = "TENDER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int CardsPlayedThisTurn { get; private set; }

    public int DisplayAmount => CardsPlayedThisTurn;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner as PlayerState))
        {
            return;
        }
        CardsPlayedThisTurn++;
        var applier = GetApplier(cs);
        PowerCmd.Apply<StrengthPower>(cs, Owner, -1m, applier, null, silent: true);
        PowerCmd.Apply<DexterityPower>(cs, Owner, -1m, applier, null, silent: true);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, Owner, CardsPlayedThisTurn, GetApplier(cs), null, silent: true);
        PowerCmd.Apply<DexterityPower>(cs, Owner, CardsPlayedThisTurn, GetApplier(cs), null, silent: true);
        CardsPlayedThisTurn = 0;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(CardsPlayedThisTurn);
    }
}
