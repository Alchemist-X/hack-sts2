namespace Sts2Sim.Core.Combat.Pipeline;

public static partial class CreatureCmd
{
    public static DamageResult Damage(RunState rs, CreatureState target, decimal amount, ValueProp props)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(target);
        var cs = rs.Combat;
        if (cs != null)
        {
            var results = Damage(cs, target, amount, props, null, null, null);
            return results.Count > 0 ? results[0] : new DamageResult(target, props);
        }
        if (target.IsDead)
        {
            return new DamageResult(target, props);
        }
        List<AbstractModel>? modifiers = null;
        decimal modifiedAmount = Hook.ModifyDamage(rs, null, target, null, amount, props, null, null, ModifyDamageHookType.All, ref modifiers);
        Hook.AfterModifyingDamageAmount(rs, null, null, modifiers);
        Hook.BeforeDamageReceived(rs, null, target, modifiedAmount, props, null, null);
        decimal blockedDamage = CreatureInternals.DamageBlockInternal(target, modifiedAmount, props);
        modifiers = null;
        decimal unblockedDamage = Hook.ModifyHpLost(rs, null, target, Math.Max(modifiedAmount - blockedDamage, 0m), props, null, null, HpLossHookPhase.BeforeOsty, ref modifiers);
        Hook.AfterModifyingHpLostBeforeOsty(rs, null, modifiers);
        modifiers = null;
        unblockedDamage = Hook.ModifyHpLost(rs, null, target, unblockedDamage, props, null, null, HpLossHookPhase.AfterOsty, ref modifiers);
        Hook.AfterModifyingHpLostAfterOsty(rs, null, modifiers);
        var result = CreatureInternals.LoseHpInternal(target, unblockedDamage, props);
        result.BlockedDamage = (int)blockedDamage;
        if (result.UnblockedDamage > 0)
        {
            Hook.AfterCurrentHpChanged(rs, null, target, -result.UnblockedDamage);
        }
        if (!result.WasTargetKilled || !target.IsDead)
        {
            Hook.AfterDamageReceived(rs, null, target, result, props, null, null);
        }
        return result;
    }

    public static void Heal(RunState rs, CreatureState creature, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var cs = rs.Combat;
        if (cs != null)
        {
            Heal(cs, creature, amount);
            return;
        }
        CreatureInternals.HealInternal(creature, amount);
        if (amount > 0m)
        {
            Hook.AfterCurrentHpChanged(rs, null, creature, amount);
        }
    }

    public static void GainMaxHp(RunState rs, CreatureState creature, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var cs = rs.Combat;
        if (cs != null)
        {
            GainMaxHp(cs, creature, amount);
            return;
        }
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative. Use LoseMaxHp for max HP loss.");
        }
        int oldMaxHp = creature.MaxHp;
        CreatureInternals.SetMaxHpInternal(creature, creature.MaxHp + amount);
        Heal(rs, creature, creature.MaxHp - oldMaxHp);
    }
}
