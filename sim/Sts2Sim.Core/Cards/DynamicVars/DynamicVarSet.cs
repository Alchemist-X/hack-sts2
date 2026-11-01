namespace Sts2Sim.Core.Cards;

public sealed class DynamicVarSet : ICanonical
{
    public const string BlockName = BlockVar.DefaultName;
    public const string CalculatedBlockName = CalculatedBlockVar.DefaultName;
    public const string CalculatedDamageName = CalculatedDamageVar.DefaultName;
    public const string CalculationBaseName = CalculationBaseVar.DefaultName;
    public const string CalculationExtraName = CalculationExtraVar.DefaultName;
    public const string CardsName = CardsVar.DefaultName;
    public const string DamageName = DamageVar.DefaultName;
    public const string DexterityName = "DexterityPower";
    public const string DoomName = "DoomPower";
    public const string EnergyName = EnergyVar.DefaultName;
    public const string ExtraDamageName = ExtraDamageVar.DefaultName;
    public const string ForgeName = ForgeVar.DefaultName;
    public const string GoldName = GoldVar.DefaultName;
    public const string HealName = HealVar.DefaultName;
    public const string HpLossName = HpLossVar.DefaultName;
    public const string MaxHpName = MaxHpVar.DefaultName;
    public const string OstyDamageName = OstyDamageVar.DefaultName;
    public const string PoisonName = "PoisonPower";
    public const string RepeatName = RepeatVar.DefaultName;
    public const string StarsName = StarsVar.DefaultName;
    public const string StrengthName = "StrengthPower";
    public const string SummonName = SummonVar.DefaultName;
    public const string VulnerableName = "VulnerablePower";
    public const string WeakName = "WeakPower";

    private static readonly DynamicVar[] NoVars = Array.Empty<DynamicVar>();

    private readonly DynamicVar[] _vars;

    public DynamicVarSet(IEnumerable<DynamicVar> vars)
    {
        ArgumentNullException.ThrowIfNull(vars);
        var list = new List<DynamicVar>();
        foreach (var current in vars)
        {
            for (int j = 0; j < list.Count; j++)
            {
                if (list[j].Name == current.Name)
                {
                    throw new ArgumentException($"DynamicVarSet contains duplicate key '{current.Name}'. Give each var of the same type its own name.");
                }
            }
            list.Add(current);
        }
        _vars = list.Count == 0 ? NoVars : list.ToArray();
    }

    private DynamicVarSet(DynamicVar[] vars)
    {
        _vars = vars;
    }

    public static DynamicVarSet Empty { get; } = new(NoVars);

    public int Count => _vars.Length;

    public IReadOnlyList<DynamicVar> All => _vars;

    public DynamicVar this[string name] => TryGet(name) ?? throw new KeyNotFoundException(name);

    public BlockVar Block => (BlockVar)this[BlockName];

    public CalculatedBlockVar CalculatedBlock => (CalculatedBlockVar)this[CalculatedBlockName];

    public CalculatedDamageVar CalculatedDamage => (CalculatedDamageVar)this[CalculatedDamageName];

    public CalculationBaseVar CalculationBase => (CalculationBaseVar)this[CalculationBaseName];

    public CalculationExtraVar CalculationExtra => (CalculationExtraVar)this[CalculationExtraName];

    public CardsVar Cards => (CardsVar)this[CardsName];

    public DamageVar Damage => (DamageVar)this[DamageName];

    public DynamicVar Dexterity => this[DexterityName];

    public DynamicVar Doom => this[DoomName];

    public EnergyVar Energy => (EnergyVar)this[EnergyName];

    public ExtraDamageVar ExtraDamage => (ExtraDamageVar)this[ExtraDamageName];

    public ForgeVar Forge => (ForgeVar)this[ForgeName];

    public GoldVar Gold => (GoldVar)this[GoldName];

    public HealVar Heal => (HealVar)this[HealName];

    public HpLossVar HpLoss => (HpLossVar)this[HpLossName];

    public MaxHpVar MaxHp => (MaxHpVar)this[MaxHpName];

    public OstyDamageVar OstyDamage => (OstyDamageVar)this[OstyDamageName];

    public DynamicVar Poison => this[PoisonName];

    public RepeatVar Repeat => (RepeatVar)this[RepeatName];

    public StarsVar Stars => (StarsVar)this[StarsName];

    public DynamicVar Strength => this[StrengthName];

    public SummonVar Summon => (SummonVar)this[SummonName];

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

    public void InitializeWithOwner()
    {
        var vars = _vars;
        for (int i = 0; i < vars.Length; i++)
        {
            if (vars[i] is CalculatedVar calculated)
            {
                calculated.UpdateValues(this);
            }
        }
    }

    public void FinalizeUpgrade()
    {
        var vars = _vars;
        for (int i = 0; i < vars.Length; i++)
        {
            vars[i].FinalizeUpgrade();
        }
    }

    public void RecalculateForUpgradeOrEnchant()
    {
        var vars = _vars;
        for (int i = 0; i < vars.Length; i++)
        {
            if (vars[i] is CalculatedVar calculated)
            {
                calculated.RecalculateForUpgradeOrEnchant(this);
            }
        }
    }

    public DynamicVarSet CloneForOwner()
    {
        var clone = DeepClone();
        clone.InitializeWithOwner();
        return clone;
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
        return new DynamicVarSet(copy);
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
