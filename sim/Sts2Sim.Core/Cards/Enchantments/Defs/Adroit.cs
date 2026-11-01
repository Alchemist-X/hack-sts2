namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Adroit : EnchantmentModel
{
    public const string EnchantmentId = "ADROIT";
    public const decimal BaseBlock = 0m;

    public override string Id => EnchantmentId;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
        CreatureCmd.GainBlock(cs, Card!.Owner, DynamicVars.Block, cardPlay);
    }

    public override void RecalculateValues()
    {
        DynamicVars.Block.BaseValue = Amount;
    }
}
