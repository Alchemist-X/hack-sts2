namespace Sts2Sim.Core.Powers;

public sealed class BattlewornDummyTimeLimitPower : PowerModel
{
    public const string PowerId = "BATTLEWORN_DUMMY_TIME_LIMIT_POWER";

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
        if (BattlewornDummyEventEncounter.IsBattlewornDummyEncounter(cs))
        {
            BattlewornDummyEventEncounter.SetRanOutOfTime(cs, true);
        }
        CreatureCmd.Escape(cs, Owner);
    }
}
