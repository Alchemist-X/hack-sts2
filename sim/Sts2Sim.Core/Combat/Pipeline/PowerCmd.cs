namespace Sts2Sim.Core.Combat.Pipeline;

public static class PowerCmd
{
    public static T? Apply<T>(CombatState cs, CreatureState target, decimal amount, CreatureState? applier, CardInstance? cardSource, bool silent = false)
        where T : PowerModel, new()
    {
        if (CombatStatus.IsEnding(cs))
        {
            return null;
        }
        if (!CombatStatus.CanReceivePowers(cs, target))
        {
            return null;
        }
        var canonical = new T();
        var power = FindExistingInstanceForStacking(canonical, target, applier);
        if (power == null)
        {
            power = canonical;
            Apply(cs, power, target, amount, applier, cardSource, silent);
        }
        else if (ModifyAmount(cs, power, amount, applier, cardSource, silent) == 0)
        {
            power = null;
        }
        return power as T;
    }

    public static void Apply<T>(CombatState cs, IReadOnlyList<CreatureState> targets, decimal amount, CreatureState? applier, CardInstance? cardSource, bool silent = false)
        where T : PowerModel, new()
    {
        for (int i = 0; i < targets.Count; i++)
        {
            Apply<T>(cs, targets[i], amount, applier, cardSource, silent);
        }
    }

    public static void Apply(CombatState cs, PowerModel power, CreatureState target, decimal amount, CreatureState? applier, CardInstance? cardSource, bool silent = false)
    {
        if (CombatStatus.IsEnding(cs) || amount == 0m || !CombatStatus.CanReceivePowers(cs, target))
        {
            return;
        }
        var existing = FindExistingInstanceForStacking(power, target, applier);
        if (existing != null)
        {
            ModifyAmount(cs, existing, amount, applier, cardSource);
            return;
        }
        power.ApplierId = applier?.CombatId ?? PowerModel.NoCreature;
        Hook.BeforePowerAmountChanged(cs, power, amount, target, applier, cardSource);
        decimal modifiedAmount = amount;
        List<AbstractModel>? givenModifiers = null;
        bool hasGivenModifiers = false;
        if (applier != null && CombatStatus.IsAttached(cs, applier))
        {
            modifiedAmount = Hook.ModifyPowerAmountGiven(cs, power, applier, modifiedAmount, target, cardSource, ref givenModifiers);
            hasGivenModifiers = true;
        }
        List<AbstractModel>? receivedModifiers = null;
        modifiedAmount = Hook.ModifyPowerAmountReceived(cs, power, target, modifiedAmount, applier, ref receivedModifiers);
        power.BeforeApplied(cs, target, modifiedAmount, applier, cardSource);
        if (!CombatStatus.CanReceivePowers(cs, target))
        {
            return;
        }
        power.ApplyInternal(target, modifiedAmount);
        if (target.Side == CombatSide.Player && power.Type == PowerType.Debuff)
        {
            power.SkipNextDurationTick = true;
        }
        if (hasGivenModifiers)
        {
            Hook.AfterModifyingPowerAmountGiven(cs, givenModifiers, power);
        }
        Hook.AfterModifyingPowerAmountReceived(cs, receivedModifiers, power);
        if (modifiedAmount != 0m)
        {
            power.AfterApplied(cs, applier, cardSource);
            Hook.AfterPowerAmountChanged(cs, power, modifiedAmount, applier, cardSource);
        }
    }

    public static PowerModel? FindExistingInstanceForStacking(PowerModel basePower, CreatureState target, CreatureState? applier)
    {
        switch (basePower.InstanceType)
        {
            case PowerInstanceType.Instanced:
                return null;
            case PowerInstanceType.InstancedPerApplier:
            {
                int applierId = applier?.CombatId ?? PowerModel.NoCreature;
                var powers = target.Powers;
                for (int i = 0; i < powers.Count; i++)
                {
                    var p = powers[i];
                    if (p.Id == basePower.Id && p.ApplierId == applierId)
                    {
                        return p;
                    }
                }
                return null;
            }
            case PowerInstanceType.None:
                return target.GetPower(basePower.Id);
            default:
                throw new ArgumentOutOfRangeException(nameof(basePower), basePower.InstanceType, "InstanceType");
        }
    }

    public static int ModifyAmount(CombatState cs, PowerModel power, decimal offset, CreatureState? applier, CardInstance? cardSource, bool silent = false)
    {
        if (CombatStatus.IsEnding(cs))
        {
            return 0;
        }
        var owner = power.Owner;
        if (!CombatStatus.IsAttached(cs, owner))
        {
            return 0;
        }
        Hook.BeforePowerAmountChanged(cs, power, offset, owner, applier, cardSource);
        decimal modifiedOffset = offset;
        List<AbstractModel>? givenModifiers = null;
        bool hasGivenModifiers = false;
        if (applier != null && CombatStatus.IsAttached(cs, applier))
        {
            modifiedOffset = Hook.ModifyPowerAmountGiven(cs, power, applier, modifiedOffset, owner, cardSource, ref givenModifiers);
            hasGivenModifiers = true;
        }
        List<AbstractModel>? receivedModifiers = null;
        modifiedOffset = Hook.ModifyPowerAmountReceived(cs, power, owner, modifiedOffset, applier, ref receivedModifiers);
        int newAmount = power.Amount + (int)modifiedOffset;
        power.SetAmount(newAmount);
        if (hasGivenModifiers)
        {
            Hook.AfterModifyingPowerAmountGiven(cs, givenModifiers, power);
        }
        Hook.AfterModifyingPowerAmountReceived(cs, receivedModifiers, power);
        if ((int)modifiedOffset != 0)
        {
            Hook.AfterPowerAmountChanged(cs, power, modifiedOffset, applier, cardSource);
        }
        if (power.ShouldRemoveDueToAmount())
        {
            Remove(cs, power);
        }
        return newAmount;
    }

    public static void Decrement(CombatState cs, PowerModel power)
    {
        ModifyAmount(cs, power, -1m, null, null);
    }

    public static void TickDownDuration(CombatState cs, PowerModel power)
    {
        if (!power.SkipNextDurationTick)
        {
            Decrement(cs, power);
            return;
        }
        power.SkipNextDurationTick = false;
    }

    public static void Remove<T>(CombatState cs, CreatureState creature) where T : PowerModel
    {
        Remove(cs, creature.GetPower<T>());
    }

    public static void Remove(CombatState cs, PowerModel? power)
    {
        if (power == null)
        {
            return;
        }
        power.RemoveInternal();
        power.AfterRemoved(cs, power.Owner);
    }
}
