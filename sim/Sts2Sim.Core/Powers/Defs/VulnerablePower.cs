namespace Sts2Sim.Core.Powers;

public sealed class VulnerablePower : PowerModel
{
    public const string PowerId = "VULNERABLE_POWER";

    public const decimal DamageIncrease = 1.5m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(CombatState cs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (target == null || !ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        decimal num = DamageIncrease;
        if (dealer != null && dealer.GetPower(MultiplierModifierIds.CrueltyPower) is IVulnerableMultiplierModifier cruelty)
        {
            num = cruelty.ModifyVulnerableMultiplier(cs, target, num, props, dealer, cardSource);
        }
        if (target.GetPower(MultiplierModifierIds.DebilitatePower) is IVulnerableMultiplierModifier debilitate)
        {
            num = debilitate.ModifyVulnerableMultiplier(cs, target, num, props, dealer, cardSource);
        }
        return num;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side)
    {
        if (side == CombatSide.Enemy)
        {
            PowerCmd.TickDownDuration(cs, this);
        }
    }
}
