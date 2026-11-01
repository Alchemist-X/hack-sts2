namespace Sts2Sim.Core.Powers;

public sealed class CrabRagePower : PowerModel
{
    public const string PowerId = "CRAB_RAGE_POWER";

    public const int StrengthAmount = 6;

    public const decimal BlockAmount = 99m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (ReferenceEquals(creature, Owner) || creature.Side != Owner.Side)
        {
            return;
        }
        var cs = rs.Combat!;
        PowerCmd.Apply<StrengthPower>(cs, Owner, StrengthAmount, Owner, null);
        CreatureCmd.GainBlock(cs, Owner, BlockAmount, ValueProp.Unpowered, null);
        PowerCmd.Remove(cs, this);
    }
}
