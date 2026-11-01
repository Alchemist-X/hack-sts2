namespace Sts2Sim.Core.Relics;

public sealed class CaptainsWheel : RelicModel
{
    public const string RelicId = "CAPTAINS_WHEEL";

    public const int BlockTurn = 3;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(18m, ValueProp.Unpowered)
    };

    public override void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
        if (ReferenceEquals(creature, Owner) && Owner.TurnNumber == BlockTurn)
        {
            CreatureCmd.GainBlock(cs, Owner, DynamicVars.Block, null);
        }
    }
}
