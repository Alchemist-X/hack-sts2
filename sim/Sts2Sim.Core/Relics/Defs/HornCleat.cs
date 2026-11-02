namespace Sts2Sim.Core.Relics;

public sealed class HornCleat : RelicModel
{
    public const string RelicId = "HORN_CLEAT";

    public const int BlockTurn = 2;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(14m, ValueProp.Unpowered)
    };

    public override void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
        if (ReferenceEquals(creature, Owner) && Owner.TurnNumber == BlockTurn)
        {
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
        }
    }
}
