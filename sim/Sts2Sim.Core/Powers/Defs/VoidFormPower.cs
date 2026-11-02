namespace Sts2Sim.Core.Powers;

public sealed class VoidFormPower : PowerModel
{
    public const string PowerId = "VOID_FORM_POWER";

    public const int HiddenCardsPlayedThisTurn = 999999999;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int CardsPlayedThisTurn { get; private set; }

    public override void BeforePowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState target, CreatureState? applier, CardInstance? cardSource)
    {
        if (!ReferenceEquals(power, this))
        {
            return;
        }
        HideTemporaryZeroCostVisual();
    }

    public override void BeforeApplied(CombatState cs, CreatureState target, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        HideTemporaryZeroCostVisual();
    }

    public override bool TryModifyEnergyCostInCombatLate(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (ShouldSkip(cs, card))
        {
            return false;
        }
        modifiedCost = 0m;
        return true;
    }

    public override bool TryModifyStarCost(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (ShouldSkip(cs, card))
        {
            return false;
        }
        modifiedCost = 0m;
        return true;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner) && !cardPlay.IsAutoPlay && cardPlay.IsLastInSeries)
        {
            CardsPlayedThisTurn++;
        }
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        CardsPlayedThisTurn = 0;
    }

    private bool ShouldSkip(CombatState cs, CardInstance card)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return true;
        }
        var pileType = card.GetPileType(cs);
        if (pileType != PileType.Hand && pileType != PileType.Play)
        {
            return true;
        }
        return CardsPlayedThisTurn >= Amount;
    }

    private void HideTemporaryZeroCostVisual()
    {
        CardsPlayedThisTurn = HiddenCardsPlayedThisTurn;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(CardsPlayedThisTurn);
    }
}
