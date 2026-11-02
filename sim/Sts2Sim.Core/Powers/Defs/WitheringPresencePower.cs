namespace Sts2Sim.Core.Powers;

public sealed class WitheringPresencePower : PowerModel
{
    public const string PowerId = "WITHERING_PRESENCE_POWER";

    public const int BaseCardsLeft = 6;

    public const string WitherCardId = "WITHER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public decimal CardsLeft { get; private set; } = BaseCardsLeft;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var target = GetTarget(cs);
        var targetPlayer = target as PlayerState ?? (target != null ? CombatStatus.PetOwnerOf(cs, target) : null);
        if (!ReferenceEquals(cardPlay.Card.Owner, targetPlayer))
        {
            return;
        }
        CardsLeft -= 1m;
        if ((int)CardsLeft > 0)
        {
            return;
        }
        CardPileCmd.AddToCombatAndPreview(cs, cardPlay.Card.Owner, WitherCardId, PileType.Hand, 1, null);
        CardsLeft = BaseCardsLeft;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(CardsLeft);
    }
}
