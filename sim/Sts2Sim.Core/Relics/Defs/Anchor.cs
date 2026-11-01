namespace Sts2Sim.Core.Relics;

public sealed class Anchor : RelicModel
{
    public const string RelicId = "ANCHOR";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(10m, ValueProp.Unpowered)
    };

    public override void BeforeCombatStart(RunState rs)
    {
        CreatureCmd.GainBlock(rs.Combat!, Owner, DynamicVars.Block, null);
    }
}
