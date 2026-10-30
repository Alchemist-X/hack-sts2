namespace Sts2Sim.Core.Combat.Pipeline;

public static class CreatureInternals
{
    public const int MaxBlock = 999999999;

    public const int MaxHpCap = 999999999;

    public const decimal MaxHpLossPerHit = 999999999m;

    public static void SetBlock(CreatureState creature, int value)
    {
        if (value < 0)
        {
            throw new ArgumentException("Block must be positive", nameof(value));
        }
        creature.Block = value;
    }

    public static void SetCurrentHp(CreatureState creature, int value)
    {
        if (value < 0)
        {
            throw new ArgumentException("Current HP must be positive", nameof(value));
        }
        creature.CurrentHp = value;
    }

    public static decimal DamageBlockInternal(CreatureState creature, decimal amount, ValueProp props)
    {
        decimal num = (props & ValueProp.Unblockable) != 0 ? 0m : Math.Min(creature.Block, amount);
        SetBlock(creature, creature.Block - (int)num);
        return num;
    }

    public static DamageResult LoseHpInternal(CreatureState creature, decimal amount, ValueProp props)
    {
        bool killed = creature.CurrentHp > 0 && amount >= creature.CurrentHp;
        int currentHp = creature.CurrentHp;
        int num = (int)Math.Clamp(amount, 0m, MaxHpLossPerHit);
        SetCurrentHp(creature, Math.Max(creature.CurrentHp - num, 0));
        return new DamageResult(creature, props)
        {
            UnblockedDamage = currentHp - creature.CurrentHp,
            WasTargetKilled = killed,
            OverkillDamage = killed ? Math.Max(num - currentHp, 0) : 0
        };
    }

    public static void GainBlockInternal(CreatureState creature, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be positive. Use LoseBlock for block loss.");
        }
        SetBlock(creature, (int)Math.Min(creature.Block + amount, MaxBlock));
    }

    public static void LoseBlockInternal(CreatureState creature, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be positive. Use GainBlock for block gain.");
        }
        SetBlock(creature, (int)Math.Max(creature.Block - amount, 0m));
    }

    public static void HealInternal(CreatureState creature, decimal amount)
    {
        bool wasDead = creature.IsDead;
        SetCurrentHpInternal(creature, creature.CurrentHp + amount);
        if (wasDead && !creature.IsDead && creature is PlayerState player)
        {
            player.ActivateHooks();
        }
    }

    public static void SetCurrentHpInternal(CreatureState creature, decimal amount)
    {
        SetCurrentHp(creature, (int)Math.Min(amount, creature.MaxHp));
    }

    public static void SetMaxHpInternal(CreatureState creature, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative.");
        }
        creature.MaxHp = Math.Min((int)amount, MaxHpCap);
        SetCurrentHp(creature, Math.Min(creature.CurrentHp, creature.MaxHp));
    }

    public static void BeforeTurnStart(CreatureState creature)
    {
        var powers = creature.Powers;
        for (int i = 0; i < powers.Count; i++)
        {
            powers[i].AmountOnTurnStart = powers[i].Amount;
        }
    }

    public static void RemoveAllPowersAfterDeath(CombatState cs, CreatureState creature, List<PowerModel> removed)
    {
        removed.Clear();
        var powers = creature.Powers;
        int n = powers.Count;
        if (n == 0)
        {
            return;
        }
        var keep = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var p = powers[i];
            keep[i] = !p.ShouldPowerBeRemovedAfterOwnerDeath() || !Hook.ShouldPowerBeRemovedOnDeath(cs, p);
        }
        for (int i = 0; i < n; i++)
        {
            if (!keep[i])
            {
                removed.Add(powers[i]);
            }
        }
        for (int i = 0; i < removed.Count; i++)
        {
            removed[i].RemoveInternal();
        }
    }

    public static void RemoveAllPowersInternal(CreatureState creature, List<PowerModel> removed)
    {
        removed.Clear();
        removed.AddRange(creature.Powers);
        for (int i = 0; i < removed.Count; i++)
        {
            removed[i].RemoveInternal();
        }
    }
}
