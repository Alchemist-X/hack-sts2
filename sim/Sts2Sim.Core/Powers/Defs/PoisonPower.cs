namespace Sts2Sim.Core.Powers;

public sealed class PoisonPower : PowerModel
{
    public const string PowerId = "POISON_POWER";

    public const string AccelerantPowerId = "ACCELERANT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int TriggerCount(CombatState cs)
    {
        var opponents = new List<CreatureState>();
        CombatStatus.GetOpponentsOf(cs, Owner, opponents);
        int accelerant = 0;
        for (int i = 0; i < opponents.Count; i++)
        {
            if (opponents[i].IsAlive)
            {
                accelerant += opponents[i].GetPower(AccelerantPowerId)?.Amount ?? 0;
            }
        }
        return Math.Min(Amount, 1 + accelerant);
    }

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            Trigger(cs);
        }
    }

    public void Trigger(CombatState cs)
    {
        int iterations = TriggerCount(cs);
        for (int i = 0; i < iterations; i++)
        {
            using (cs.Choice.PushContext(ChoiceContextKind.Throwing))
            {
                CreatureCmd.Damage(cs, Owner, Amount, ValueProp.Unblockable | ValueProp.Unpowered, null, null, null);
            }
            if (Owner.IsAlive)
            {
                PowerCmd.Decrement(cs, this);
            }
        }
    }
}
