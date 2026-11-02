namespace Sts2Sim.Core.Cards;

public class CalculatedVar : DynamicVar
{
    private Func<CombatState, CardInstance, CreatureState?, decimal>? _multiplierCalc;

    public CalculatedVar(string name)
        : base(name, 0m)
    {
    }

    protected CalculatedVar(string name, ValueProp props)
        : base(name, 0m, props)
    {
    }

    public bool HasMultiplier => _multiplierCalc != null;

    public CalculatedVar WithMultiplier(Func<CombatState, CardInstance, CreatureState?, decimal> multiplierCalc)
    {
        ArgumentNullException.ThrowIfNull(multiplierCalc);
        if (_multiplierCalc != null)
        {
            throw new InvalidOperationException($"Tried to set extra multiplier calc on {Name} twice!");
        }
        if (multiplierCalc.Target is CardDef or CardInstance or AbstractModel)
        {
            throw new InvalidOperationException("Multiplier calc must be static!");
        }
        _multiplierCalc = multiplierCalc;
        return this;
    }

    public decimal Calculate(CombatState cs, CardInstance card, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        var calc = _multiplierCalc ?? throw new InvalidOperationException("Extra multiplier calc must be specified!");
        decimal num = CombatStatus.IsInProgress(cs) && card.IsInCombat(cs) ? calc(cs, card, target) : 0m;
        var vars = card.Vars;
        return GetBaseVar(vars).BaseValue + GetExtraVar(vars).BaseValue * num;
    }

    public void RecalculateForUpgradeOrEnchant(DynamicVarSet vars)
    {
        ArgumentNullException.ThrowIfNull(vars);
        decimal baseValue = GetBaseVar(vars).BaseValue;
        if (baseValue != BaseValue)
        {
            WasJustUpgraded = true;
        }
        BaseValue = baseValue;
    }

    public void UpdateValues(DynamicVarSet vars)
    {
        ArgumentNullException.ThrowIfNull(vars);
        BaseValue = GetBaseVar(vars).BaseValue;
    }

    protected virtual DynamicVar GetBaseVar(DynamicVarSet vars) => vars.CalculationBase;

    protected virtual DynamicVar GetExtraVar(DynamicVarSet vars) => vars.CalculationExtra;
}
