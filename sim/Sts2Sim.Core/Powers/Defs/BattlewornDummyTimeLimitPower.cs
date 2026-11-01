namespace Sts2Sim.Core.Powers;

public sealed class BattlewornDummyTimeLimitPower : PowerModel
{
    public const string PowerId = "BATTLEWORN_DUMMY_TIME_LIMIT_POWER";

    public const string BattlewornDummyEventEncounterId = "BATTLEWORN_DUMMY_EVENT_ENCOUNTER";

    public const string RanOutOfTimeSite = "BATTLEWORN_DUMMY_TIME_LIMIT_POWER: BattlewornDummyEventEncounter.RanOutOfTime = true has no encounter state in the kernel (Models.Powers/BattlewornDummyTimeLimitPower.cs:83-86)";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Amount > 1)
        {
            PowerCmd.Decrement(cs, this);
            return;
        }
        if (cs.EncounterId == BattlewornDummyEventEncounterId)
        {
            cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, RanOutOfTimeSite);
        }
        CreatureCmd.Escape(cs, Owner);
    }
}
