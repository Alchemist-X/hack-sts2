namespace Sts2Sim.Core.Powers;

public sealed class VeilpiercerPower : PowerModel
{
    public const string PowerId = "VEILPIERCER_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) || !CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Ethereal))
        {
            return;
        }
        if (IsInHandOrPlay(cs, card))
        {
            PowerCmd.Decrement(cs, this);
        }
    }

    public override bool TryModifyEnergyCostInCombatLate(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return false;
        }
        if (!CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Ethereal))
        {
            return false;
        }
        if (!IsInHandOrPlay(cs, card))
        {
            return false;
        }
        modifiedCost = 0m;
        return true;
    }

    private static bool IsInHandOrPlay(CombatState cs, CardInstance card)
    {
        var pile = card.GetCombatPile(cs);
        return pile != null && (pile.Type == PileType.Hand || pile.Type == PileType.Play);
    }
}
