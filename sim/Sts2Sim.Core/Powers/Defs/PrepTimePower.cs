namespace Sts2Sim.Core.Powers;

public sealed class PrepTimePower : PowerModel
{
    public const string PowerId = "PREP_TIME_POWER";

    public const string VigorPowerId = "VIGOR_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
            PowerCmd.Apply(cs, PowerRegistry.Create(VigorPowerId), Owner, Amount, Owner, null);
        }
    }
}
