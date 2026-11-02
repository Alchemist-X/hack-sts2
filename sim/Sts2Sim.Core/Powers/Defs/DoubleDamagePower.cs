namespace Sts2Sim.Core.Powers;

public sealed class DoubleDamagePower : PowerModel
{
    public const string PowerId = "DOUBLE_DAMAGE_POWER";

    public const decimal DamageMultiplier = 2m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(dealer, Owner) && !IsOwnersPet(rs.Combat!, dealer))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        if (cardSource == null)
        {
            return 1m;
        }
        return DamageMultiplier;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }

    private bool IsOwnersPet(CombatState cs, CreatureState? dealer)
        => dealer != null && ReferenceEquals(CombatStatus.PetOwnerOf(cs, dealer), Owner);
}
