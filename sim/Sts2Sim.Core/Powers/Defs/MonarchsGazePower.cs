namespace Sts2Sim.Core.Powers;

public sealed class MonarchsGazePower : PowerModel
{
    public const string PowerId = "MONARCHS_GAZE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        if (ReferenceEquals(dealer, Owner) && props.IsPoweredAttack())
        {
            PowerCmd.Apply<MonarchsGazeStrengthDownPower>(cs, target, Amount, Owner, null);
        }
    }
}
