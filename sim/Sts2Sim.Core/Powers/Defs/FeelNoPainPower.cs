namespace Sts2Sim.Core.Powers;

public sealed class FeelNoPainPower : PowerModel
{
    public const string PowerId = "FEEL_NO_PAIN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        if (ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        }
    }
}
