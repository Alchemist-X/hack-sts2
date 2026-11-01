namespace Sts2Sim.Core.Powers;

public sealed class FreeAttackPower : PowerModel
{
    public const string PowerId = "FREE_ATTACK_POWER";

    public const CardType FreeCardType = CardType.Attack;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool TryModifyEnergyCostInCombatLate(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return false;
        }
        if (card.Type != FreeCardType)
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

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) && card.Type == FreeCardType && IsInHandOrPlay(cs, card))
        {
            PowerCmd.Decrement(cs, this);
        }
    }

    private static bool IsInHandOrPlay(CombatState cs, CardInstance card)
    {
        var pileType = card.GetPileType(cs);
        return pileType == PileType.Hand || pileType == PileType.Play;
    }
}
