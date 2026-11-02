namespace Sts2Sim.Core.Powers;

public sealed class ShadowStepPower : PowerModel
{
    public const string PowerId = "SHADOW_STEP_POWER";

    public const string DoubleDamagePowerId = "DOUBLE_DAMAGE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        using (cs.Choice.PushContext(ChoiceContextKind.Throwing))
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(DoubleDamagePowerId), Owner, Amount, Owner, null);
        }
        PowerCmd.Remove(cs, this);
    }
}
