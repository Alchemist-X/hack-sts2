namespace Sts2Sim.Core.Relics;

public sealed class TheAbacus : RelicModel
{
    public const string RelicId = "THE_ABACUS";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(6m, ValueProp.Unpowered)
    };

    public override void AfterShuffle(CombatState cs, PlayerState shuffler)
    {
        if (!ReferenceEquals(shuffler, Owner))
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
    }
}
