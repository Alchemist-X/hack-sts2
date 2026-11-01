namespace Sts2Sim.Core.Powers;

public sealed class ShroudPower : PowerModel
{
    public const string PowerId = "SHROUD_POWER";

    public const string DoomPowerId = "DOOM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (ReferenceEquals(applier, Owner) && power.Id == DoomPowerId)
        {
            CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        }
    }
}
