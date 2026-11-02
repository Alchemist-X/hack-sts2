namespace Sts2Sim.Core.Powers;

public sealed class ViciousPower : PowerModel
{
    public const string PowerId = "VICIOUS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (amount <= 0m || !ReferenceEquals(applier, Owner) || power is not VulnerablePower)
        {
            return;
        }
        CardPileCmd.Draw(cs, Amount, OwnerPlayer());
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} draws for its owner's player (Models.Powers/ViciousPower.cs), but its owner is not a player");
}
