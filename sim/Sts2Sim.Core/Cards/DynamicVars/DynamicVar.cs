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

    public override string ToString() => IntValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
