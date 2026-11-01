namespace Sts2Sim.Core.Powers;

public sealed class DevourLifePower : PowerModel
{
    public const string PowerId = "DEVOUR_LIFE_POWER";

    public const string SoulCardId = "SOUL";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (!string.Equals(card.Id, SoulCardId, StringComparison.Ordinal))
        {
            return;
        }
        var cardOwner = CombatStatus.CardOwner(cs, card);
        if (!ReferenceEquals(cardOwner, Owner))
        {
            return;
        }
        OstyCmd.Summon(cs, cardOwner, Amount, this);
    }
}
