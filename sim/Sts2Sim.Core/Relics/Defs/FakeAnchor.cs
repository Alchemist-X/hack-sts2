namespace Sts2Sim.Core.Relics;

public sealed class FakeAnchor : RelicModel
{
    public const string RelicId = "FAKE_ANCHOR";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override int MerchantCost => 50;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(4m, ValueProp.Unpowered)
    };

    public override void BeforeCombatStart(RunState rs)
    {
        CreatureCmd.GainBlock(rs.Combat!, Owner, DynamicVars.Block, null);
    }
}
