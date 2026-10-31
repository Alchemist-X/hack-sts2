namespace Sts2Sim.Core.Powers;

public sealed class DexterityPower : PowerModel
{
    public const string PowerId = "DEXTERITY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyBlockAdditive(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (cardSource != null)
        {
            if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardSource), Owner))
            {
                return 0m;
            }
        }
        else if (!ReferenceEquals(Owner, target))
        {
            return 0m;
        }
        if (!props.IsPoweredCardOrMonsterMoveBlock())
        {
            return 0m;
        }
        return Amount;
    }
}
