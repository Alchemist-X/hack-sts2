namespace Sts2Sim.Core.Powers;

public sealed class PagestormPower : PowerModel
{
    public const string PowerId = "PAGESTORM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) || !CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Ethereal))
        {
            return;
        }
        CardPileCmd.Draw(cs, Amount, OwnerPlayer());
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} draws for its owner's player (Models.Powers/PagestormPower.cs), but its owner is not a player");
}
