namespace Sts2Sim.Core.Cards;

public enum LocalCostType
{
    None,
    Absolute,
    Relative
}

[Flags]
public enum LocalCostModifierExpiration
{
    EndOfCombat = 0,
    EndOfTurn = 2,
    WhenPlayed = 4
}

[Flags]
public enum CostModifiers
{
    None = 0,
    Local = 2,
    Global = 4,
    All = -1
}

public sealed class LocalCostModifier : ICanonical
{
    public LocalCostModifier(int amount, LocalCostType type, LocalCostModifierExpiration expiration, bool reduceOnly)
    {
        Amount = amount;
        Type = type;
        Expiration = expiration;
        IsReduceOnly = reduceOnly;
    }

    public int Amount { get; set; }

    public LocalCostType Type { get; }

    public LocalCostModifierExpiration Expiration { get; }

    public bool IsReduceOnly { get; }

    public int Modify(int currentCost) => Type switch
    {
        LocalCostType.Absolute => IsReduceOnly ? Math.Min(currentCost, Amount) : Amount,
        LocalCostType.Relative => IsReduceOnly ? Math.Min(currentCost, currentCost + Amount) : currentCost + Amount,
        _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, null)
    };

    public LocalCostModifier Clone() => new(Amount, Type, Expiration, IsReduceOnly);

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CardCanonicalTags.LocalCostModifier);
        w.WriteInt32(Amount);
        w.WriteInt32((int)Type);
        w.WriteInt32((int)Expiration);
        w.WriteBool(IsReduceOnly);
    }
}

public sealed class CardEnergyCost : ICanonical
{
    private List<LocalCostModifier>? _localModifiers;
    private int _capturedXValue;

    public CardEnergyCost(int canonicalCost, bool costsX)
    {
        CostsX = costsX;
        Canonical = costsX ? 0 : canonicalCost;
        Base = Canonical;
    }

    public int Canonical { get; }

    public int Base { get; set; }

    public bool CostsX { get; }

    public bool WasJustUpgraded { get; set; }

    public int CapturedXValue
    {
        get
        {
            if (!CostsX)
            {
                throw new InvalidOperationException("Only X-cost cards have a captured value.");
            }
            return _capturedXValue;
        }
        set
        {
            if (!CostsX)
            {
                throw new InvalidOperationException("Only X-cost cards have a captured value.");
            }
            _capturedXValue = value;
        }
    }

    public bool HasLocalModifiers => _localModifiers is { Count: > 0 };

    public IReadOnlyList<LocalCostModifier> LocalModifiers => _localModifiers is null ? Array.Empty<LocalCostModifier>() : _localModifiers;

    public int GetWithModifiers(CostModifiers modifiers)
    {
        if (Base < 0 || CostsX)
        {
            return Base;
        }
        return Math.Max(0, ApplyLocalModifiers(Base, modifiers));
    }

    public int GetWithModifiers(CombatState cs, CardInstance card, CostModifiers modifiers)
    {
        if (Base < 0 || CostsX)
        {
            return Base;
        }
        int num = ApplyLocalModifiers(Base, modifiers);
        if ((modifiers & CostModifiers.Global) != 0)
        {
            num = (int)Hook.ModifyEnergyCostInCombat(cs, card, num);
        }
        return Math.Max(0, num);
    }

    public int GetAmountToSpend()
    {
        if (CostsX)
        {
            throw new InvalidOperationException("An X-cost card spends the player's current energy; use GetAmountToSpend(cs, card).");
        }
        return Math.Max(0, GetWithModifiers(CostModifiers.All));
    }

    public int GetAmountToSpend(CombatState cs, CardInstance card)
    {
        if (CostsX)
        {
            return cs.Player.Energy;
        }
        return Math.Max(0, GetWithModifiers(cs, card, CostModifiers.All));
    }

    public int GetResolved()
    {
        if (CostsX)
        {
            return CapturedXValue;
        }
        return Math.Max(0, GetWithModifiers(CostModifiers.All));
    }

    public int GetResolved(CombatState cs, CardInstance card)
    {
        if (CostsX)
        {
            return CapturedXValue;
        }
        return Math.Max(0, GetWithModifiers(cs, card, CostModifiers.All));
    }

    public void SetUntilPlayed(int cost, bool reduceOnly = false)
    {
        if (cost != 0 || Canonical >= 0)
        {
            AddModifier(new LocalCostModifier(cost, LocalCostType.Absolute, LocalCostModifierExpiration.WhenPlayed, reduceOnly));
        }
    }

    public void SetThisTurnOrUntilPlayed(int cost, bool reduceOnly = false)
    {
        if (cost != 0 || Canonical >= 0)
        {
            AddModifier(new LocalCostModifier(cost, LocalCostType.Absolute, LocalCostModifierExpiration.EndOfTurn | LocalCostModifierExpiration.WhenPlayed, reduceOnly));
        }
    }

    public void SetThisTurn(int cost, bool reduceOnly = false)
    {
        if (cost != 0 || Canonical >= 0)
        {
            AddModifier(new LocalCostModifier(cost, LocalCostType.Absolute, LocalCostModifierExpiration.EndOfTurn, reduceOnly));
        }
    }

    public void SetThisCombat(int cost, bool reduceOnly = false)
    {
        if (cost != 0 || Canonical >= 0)
        {
            AddModifier(new LocalCostModifier(cost, LocalCostType.Absolute, LocalCostModifierExpiration.EndOfCombat, reduceOnly));
        }
    }

    public void AddUntilPlayed(int amount, bool reduceOnly = false)
    {
        if (amount != 0)
        {
            AddModifier(new LocalCostModifier(amount, LocalCostType.Relative, LocalCostModifierExpiration.WhenPlayed, reduceOnly));
        }
    }

    public void AddThisTurnOrUntilPlayed(int amount, bool reduceOnly = false)
    {
        if (amount != 0)
        {
            AddModifier(new LocalCostModifier(amount, LocalCostType.Relative, LocalCostModifierExpiration.EndOfTurn | LocalCostModifierExpiration.WhenPlayed, reduceOnly));
        }
    }

    public void AddThisTurn(int amount, bool reduceOnly = false)
    {
        if (amount != 0)
        {
            AddModifier(new LocalCostModifier(amount, LocalCostType.Relative, LocalCostModifierExpiration.EndOfTurn, reduceOnly));
        }
    }

    public void AddThisCombat(int amount, bool reduceOnly = false)
    {
        if (amount != 0)
        {
            AddModifier(new LocalCostModifier(amount, LocalCostType.Relative, LocalCostModifierExpiration.EndOfCombat, reduceOnly));
        }
    }

    public bool EndOfTurnCleanup() => RemoveModifiersWith(LocalCostModifierExpiration.EndOfTurn) > 0;

    public bool AfterCardPlayedCleanup() => RemoveModifiersWith(LocalCostModifierExpiration.WhenPlayed) > 0;

    public void UpgradeBy(int addend)
    {
        if (CostsX || addend == 0)
        {
            return;
        }
        int oldBase = Base;
        int newBase = Math.Max(Base + addend, 0);
        WasJustUpgraded = true;
        if (newBase < oldBase && _localModifiers != null)
        {
            for (int i = 0; i < _localModifiers.Count; i++)
            {
                var modifier = _localModifiers[i];
                if (modifier.Type == LocalCostType.Absolute && modifier.Amount > newBase)
                {
                    modifier.Amount = newBase;
                }
            }
        }
        SetCustomBaseCost(newBase);
    }

    public void FinalizeUpgrade() => WasJustUpgraded = false;

    public void ResetForDowngrade() => Base = Canonical;

    public void SetCustomBaseCost(int newBaseCost) => Base = newBaseCost;

    public CardEnergyCost DeepClone()
    {
        var clone = (CardEnergyCost)MemberwiseClone();
        if (_localModifiers is { Count: > 0 })
        {
            var copy = new List<LocalCostModifier>(_localModifiers.Count);
            for (int i = 0; i < _localModifiers.Count; i++)
            {
                copy.Add(_localModifiers[i].Clone());
            }
            clone._localModifiers = copy;
        }
        else
        {
            clone._localModifiers = null;
        }
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CardCanonicalTags.EnergyCost);
        w.WriteInt32(Canonical);
        w.WriteBool(CostsX);
        w.WriteInt32(Base);
        w.WriteInt32(_capturedXValue);
        w.WriteBool(WasJustUpgraded);
        int count = _localModifiers?.Count ?? 0;
        w.WriteCount(count);
        for (int i = 0; i < count; i++)
        {
            _localModifiers![i].WriteCanonical(w);
        }
    }

    private int ApplyLocalModifiers(int cost, CostModifiers modifiers)
    {
        if ((modifiers & CostModifiers.Local) != 0 && _localModifiers != null)
        {
            for (int i = 0; i < _localModifiers.Count; i++)
            {
                cost = _localModifiers[i].Modify(cost);
            }
        }
        return cost;
    }

    private void AddModifier(LocalCostModifier modifier)
    {
        (_localModifiers ??= new List<LocalCostModifier>(2)).Add(modifier);
    }

    private int RemoveModifiersWith(LocalCostModifierExpiration flag)
    {
        var modifiers = _localModifiers;
        if (modifiers is not { Count: > 0 })
        {
            return 0;
        }
        int removed = 0;
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if ((modifiers[i].Expiration & flag) != 0)
            {
                modifiers.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }
}
