namespace Sts2Sim.Core.Powers;

public sealed class CrabRagePower : PowerModel
{
    public const string PowerId = "CRAB_RAGE_POWER";

    public const int StrengthAmount = 6;
    public const int BlockAmount = 99;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        var owner = Owner;
        if (ReferenceEquals(creature, owner) || creature.Side != owner.Side)
        {
            return;
        }
        var cs = rs.Combat!;
        PowerCmd.Apply<StrengthPower>(cs, owner, StrengthAmount, owner, null);
        CreatureCmd.GainBlock(cs, owner, BlockAmount, ValueProp.Unpowered, null);
        PowerCmd.Remove(cs, this);
    }
}
