namespace Sts2Sim.Core.Cards;

public class StringVar : DynamicVar
{
    public StringVar(string name, string baseValue = "")
        : base(name, 0m)
    {
        StringValue = baseValue;
    }

    public string StringValue { get; set; }

    public override string ToString() => StringValue;
}
