namespace Sts2Sim.Core.Relics;

public sealed class DragonFruit : RelicModel
{
    public const string RelicId = "DRAGON_FRUIT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(1m)
    };

    public override bool IsAllowed(RunState rs) => RelicCatalog.IsBeforeAct3TreasureChest(rs);

    public override void AfterGoldGained(RunState rs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner))
        {
            CreatureCmd.GainMaxHp(rs, Owner, DynamicVars.MaxHp.BaseValue);
        }
    }
}
