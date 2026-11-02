namespace Sts2Sim.Core.Relics;

public sealed class SturdyClamp : RelicModel
{
    public const string RelicId = "STURDY_CLAMP";

    public const int MaxRetainedBlock = 10;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(10m, ValueProp.Unpowered)
    };

    public override bool ShouldClearBlock(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return true;
        }
        return false;
    }

    public override void AfterPreventingBlockClear(CombatState cs, AbstractModel preventer, CreatureState creature)
    {
        if (!ReferenceEquals(this, preventer) || !ReferenceEquals(creature, Owner))
        {
            return;
        }
        int block = creature.Block;
        if (block != 0 && block > MaxRetainedBlock)
        {
            CreatureCmd.LoseBlock(cs, creature, block - MaxRetainedBlock, null);
        }
    }
}
