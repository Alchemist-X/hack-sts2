namespace Sts2Sim.Core.Powers;

public sealed class ChildOfTheStarsPower : PowerModel
{
    public const string PowerId = "CHILD_OF_THE_STARS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)
    {
        if (amount > 0 && ReferenceEquals(spender, Owner as PlayerState))
        {
            CreatureCmd.GainBlock(cs, Owner, Amount * amount, ValueProp.Unpowered, null);
        }
    }
}
