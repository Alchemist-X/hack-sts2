namespace Sts2Sim.Core.Powers;

public sealed class AutomationPower : PowerModel
{
    public const string PowerId = "AUTOMATION_POWER";

    public const int BaseCardsLeft = 10;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public int CardsLeft { get; private set; } = BaseCardsLeft;

    public int DisplayAmount => CardsLeft;

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        CardsLeft--;
        if (CardsLeft <= 0)
        {
            PlayerCmd.GainEnergy(cs, Amount, OwnerPlayer());
            CardsLeft = BaseCardsLeft;
        }
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} gains energy for its owner's player (Models.Powers/AutomationPower.cs:356), but its owner is not a player");

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(CardsLeft);
    }
}
