namespace Sts2Sim.Core.Powers;

public sealed class TheGambitPower : PowerModel
{
    public const string PowerId = "THE_GAMBIT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || !props.IsPoweredAttack() || result.UnblockedDamage <= 0)
        {
            return;
        }
        var cs = rs.Combat!;
        var owner = Owner;
        PowerCmd.Remove(cs, this);
        CreatureCmd.Kill(cs, owner);
    }
}
