namespace Sts2Sim.Core.Powers;

public sealed class WitheringPresencePower : PowerModel
{
    public const string PowerId = "WITHERING_PRESENCE_POWER";

    public const string WitherCardId = "WITHER";

    public const int BaseCardsLeft = 6;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public decimal CardsLeft { get; private set; } = BaseCardsLeft;

    public int DisplayAmount => (int)CardsLeft;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var target = GetTarget(cs);
        var cardOwner = CombatStatus.CardOwner(cs, cardPlay.Card);
        if (!ReferenceEquals(cardOwner, target as PlayerState))
        {
            return;
        }
        CardsLeft -= 1m;
        if ((int)CardsLeft <= 0)
        {
            CardPileCmd.AddToCombatAndPreview(cs, cardOwner, WitherCardId, PileType.Hand, 1, null);
            CardsLeft = BaseCardsLeft;
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(CardsLeft);
    }
}
