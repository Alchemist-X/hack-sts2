namespace Sts2Sim.Core.Powers;

public sealed class ToricToughnessPower : PowerModel
{
    public const string PowerId = "TORIC_TOUGHNESS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public decimal Block { get; private set; }

    public void SetBlock(decimal block)
    {
        Block = block;
    }

    public override void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, new BlockVar(Block, ValueProp.Unpowered), null);
        PowerCmd.Decrement(cs, this);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(Block);
    }
}
