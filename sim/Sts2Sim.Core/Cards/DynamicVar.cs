namespace Sts2Sim.Core.Cards;

public class DynamicVar : ICanonical
{
    public const decimal MaxBaseValue = 999999999m;

    private decimal _baseValue;

    public DynamicVar(string name, decimal baseValue, ValueProp props = ValueProp.None)
    {
        Name = name;
        BaseValue = baseValue;
        Props = props;
    }

    public string Name { get; }

    public decimal BaseValue
    {
        get => _baseValue;
        set => _baseValue = Math.Min(value, MaxBaseValue);
    }

    public ValueProp Props { get; }

    public bool WasJustUpgraded { get; set; }

    public int IntValue => (int)BaseValue;

    public void UpgradeValueBy(decimal addend)
    {
        BaseValue += addend;
        WasJustUpgraded = true;
    }

    public void FinalizeUpgrade() => WasJustUpgraded = false;

    public virtual DynamicVar Clone() => (DynamicVar)MemberwiseClone();

    public virtual void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CardCanonicalTags.Var);
        w.WriteString(Name);
        w.WriteDecimal(_baseValue);
        w.WriteInt32((int)Props);
        w.WriteBool(WasJustUpgraded);
    }
}

public class DamageVar : DynamicVar
{
    public const string DefaultName = "Damage";

    public DamageVar(decimal damage, ValueProp props)
        : base(DefaultName, damage, props)
    {
    }

    public DamageVar(string name, decimal damage, ValueProp props)
        : base(name, damage, props)
    {
    }
}

public class BlockVar : DynamicVar
{
    public const string DefaultName = "Block";

    public BlockVar(decimal block, ValueProp props)
        : base(DefaultName, block, props)
    {
    }

    public BlockVar(string name, decimal block, ValueProp props)
        : base(name, block, props)
    {
    }
}

public class PowerVar<T> : DynamicVar where T : PowerModel
{
    public PowerVar(decimal powerAmount)
        : base(typeof(T).Name, powerAmount)
    {
    }

    public PowerVar(string name, decimal powerAmount)
        : base(name, powerAmount)
    {
    }
}

public sealed class DynamicVarSet : ICanonical
{
    public const string BlockName = "Block";
    public const string CalculatedBlockName = "CalculatedBlock";
    public const string CalculatedDamageName = "CalculatedDamage";
    public const string CalculationBaseName = "CalculationBase";
    public const string CalculationExtraName = "CalculationExtra";
    public const string CardsName = "Cards";
    public const string DamageName = "Damage";
    public const string DexterityName = "DexterityPower";
    public const string DoomName = "DoomPower";
    public const string EnergyName = "Energy";
    public const string ExtraDamageName = "ExtraDamage";
    public const string ForgeName = "Forge";
    public const string GoldName = "Gold";
    public const string HealName = "Heal";
    public const string HpLossName = "HpLoss";
    public const string MaxHpName = "MaxHp";
    public const string OstyDamageName = "OstyDamage";
    public const string PoisonName = "PoisonPower";
    public const string RepeatName = "Repeat";
    public const string StarsName = "Stars";
    public const string StrengthName = "StrengthPower";
    public const string SummonName = "Summon";
    public const string VulnerableName = "VulnerablePower";
    public const string WeakName = "WeakPower";

    private readonly DynamicVar[] _vars;

    public DynamicVarSet(IEnumerable<DynamicVar> vars)
    {
        _vars = vars as DynamicVar[] ?? vars.ToArray();
        for (int i = 1; i < _vars.Length; i++)
        {
            for (int j = 0; j < i; j++)
            {
                if (_vars[i].Name == _vars[j].Name)
                {
                    throw new ArgumentException($"DynamicVarSet contains duplicate key '{_vars[i].Name}'. Give each var of the same type its own name.");
                }
            }
        }
    }

    private DynamicVarSet(DynamicVar[] vars, bool _)
    {
        _vars = vars;
    }

    public int Count => _vars.Length;

    public IReadOnlyList<DynamicVar> All => _vars;

    public DynamicVar this[string name] => TryGet(name) ?? throw new KeyNotFoundException(name);

    public DynamicVar Block => this[BlockName];

    public DynamicVar CalculatedBlock => this[CalculatedBlockName];

    public DynamicVar CalculatedDamage => this[CalculatedDamageName];

    public DynamicVar CalculationBase => this[CalculationBaseName];

    public DynamicVar CalculationExtra => this[CalculationExtraName];

    public DynamicVar Cards => this[CardsName];

    public DynamicVar Damage => this[DamageName];

    public DynamicVar Dexterity => this[DexterityName];

    public DynamicVar Doom => this[DoomName];

    public DynamicVar Energy => this[EnergyName];

    public DynamicVar ExtraDamage => this[ExtraDamageName];

    public DynamicVar Forge => this[ForgeName];

    public DynamicVar Gold => this[GoldName];

    public DynamicVar Heal => this[HealName];

    public DynamicVar HpLoss => this[HpLossName];

    public DynamicVar MaxHp => this[MaxHpName];

    public DynamicVar OstyDamage => this[OstyDamageName];

    public DynamicVar Poison => this[PoisonName];

    public DynamicVar Repeat => this[RepeatName];

    public DynamicVar Stars => this[StarsName];

    public DynamicVar Strength => this[StrengthName];

    public DynamicVar Summon => this[SummonName];

    public DynamicVar Vulnerable => this[VulnerableName];

    public DynamicVar Weak => this[WeakName];

    public bool ContainsKey(string name) => TryGet(name) != null;

    public DynamicVar? TryGet(string name)
    {
        var vars = _vars;
        for (int i = 0; i < vars.Length; i++)
        {
            if (vars[i].Name == name)
            {
                return vars[i];
            }
        }
        return null;
    }

    public void FinalizeUpgrade()
    {
        var vars = _vars;
        for (int i = 0; i < vars.Length; i++)
        {
            vars[i].FinalizeUpgrade();
        }
    }

    public DynamicVarSet DeepClone()
    {
        if (_vars.Length == 0)
        {
            return this;
        }
        var copy = new DynamicVar[_vars.Length];
        for (int i = 0; i < copy.Length; i++)
        {
            copy[i] = _vars[i].Clone();
        }
        return new DynamicVarSet(copy, true);
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(CardCanonicalTags.VarSet);
        w.WriteCount(_vars.Length);
        for (int i = 0; i < _vars.Length; i++)
        {
            _vars[i].WriteCanonical(w);
        }
    }
}
