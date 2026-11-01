namespace Sts2Sim.Core.Powers;

public sealed class FeralPower : PowerModel
{
    public const string PowerId = "FERAL_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int ZeroCostAttacksPlayed { get; private set; }

    public int DisplayAmount => Math.Max(0, Amount - ZeroCostAttacksPlayed);

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        var owner = Owner;
        ZeroCostAttacksPlayed = cs.History.CountOf<CardPlayStartedEntry>(e => e.CardPlay.CardType == CardType.Attack && IsPlayOfOwnerPlayer(owner, e.CardPlay) && e.CardPlay.Resources.EnergyValue == 0 && e.Stamp.HappenedThisTurn(cs));
    }

    public override CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return cardLocation;
        }
        if (card.Type != CardType.Attack)
        {
            return cardLocation;
        }
        if (resources.EnergyValue > 0)
        {
            return cardLocation;
        }
        if (card.IsDupe)
        {
            return cardLocation;
        }
        if (ZeroCostAttacksPlayed >= Amount)
        {
            return cardLocation;
        }
        return new CardLocation(PileType.Hand, CardPilePosition.Top);
    }

    public override void AfterModifyingCardPlayResultLocation(CombatState cs, CardInstance card, CardLocation cardLocation)
    {
        ZeroCostAttacksPlayed++;
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        ZeroCostAttacksPlayed = 0;
    }

    private static bool IsPlayOfOwnerPlayer(CreatureState owner, HistoryCardPlay play)
        => owner is PlayerState player && play.PlayerId == player.CombatId;

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(ZeroCostAttacksPlayed);
    }
}
