namespace Sts2Sim.Core.Relics;

public sealed class GoldenPearl : RelicModel
{
    public const string RelicId = "GOLDEN_PEARL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new GoldVar(150)
    };

    public override void AfterObtained(RunState rs)
    {
        PlayerCmd.GainGold(rs, DynamicVars.Gold.BaseValue, Owner);
    }
}
