namespace Sts2Sim.Core.Modifiers;

public abstract class ModifierModel : AbstractModel, ICanonical
{
    public abstract string Id { get; }

    public virtual bool ClearsPlayerDeck => false;

    public void OnRunCreated(RunState rs)
        => throw new NotImplementedException("ModifierModel.OnRunCreated: W2 (Models/ModifierModel.cs:60-81)");

    public void OnRunLoaded(RunState rs)
        => throw new NotImplementedException("ModifierModel.OnRunLoaded: W2 (Models/ModifierModel.cs:83-88)");

    protected virtual void AfterRunCreated(RunState rs)
    {
    }

    protected virtual void AfterRunLoaded(RunState rs)
    {
    }

    public virtual bool IsEquivalent(ModifierModel other) => GetType() == other.GetType();

    public ModifierModel DeepClone()
    {
        var clone = (ModifierModel)MemberwiseClone();
        clone.CopyMutableReferenceState(this);
        return clone;
    }

    protected virtual void CopyMutableReferenceState(ModifierModel source)
    {
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(ModifierCanonicalTags.Modifier);
        w.WriteString(Id);
        WriteExtraCanonical(w);
    }

    protected virtual void WriteExtraCanonical(CanonicalWriter w)
    {
    }

    public override string ToString() => Id;
}
