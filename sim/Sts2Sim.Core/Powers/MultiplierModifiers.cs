namespace Sts2Sim.Core.Powers;

public interface IVulnerableMultiplierModifier
{
    decimal ModifyVulnerableMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource);
}

public interface IWeakMultiplierModifier
{
    decimal ModifyWeakMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource);
}

public static class MultiplierModifierIds
{
    public const string CrueltyPower = "CRUELTY_POWER";

    public const string DebilitatePower = "DEBILITATE_POWER";
}
