namespace Sts2Sim.Core.Powers;

public sealed class WasteAwayPower : PowerModel
{
    public const string PowerId = "WASTE_AWAY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount - Amount;
    }
}
