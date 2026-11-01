namespace Sts2Sim.Core.Powers;

public sealed class NostalgiaPower : PowerModel
{
    public const string PowerId = "NOSTALGIA_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return cardLocation;
        }
        if (!IsAttackOrSkill(card.Type))
        {
            return cardLocation;
        }
        if (cardLocation.PileType != PileType.Discard)
        {
            return cardLocation;
        }
        var owner = Owner;
        int num = cs.History.CountOf<CardPlayStartedEntry>(e => e.Stamp.HappenedThisTurn(cs) && IsAttackOrSkill(e.CardPlay.CardType) && owner is PlayerState p && e.CardPlay.PlayerId == p.CombatId);
        if (num >= Amount)
        {
            return cardLocation;
        }
        return new CardLocation(PileType.Draw, CardPilePosition.Top);
    }

    private static bool IsAttackOrSkill(CardType type) => (uint)(type - 1) <= 1u;
}
